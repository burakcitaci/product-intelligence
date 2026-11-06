using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductHub.Application.Content;
using ProductHub.Application.Products;
using ProductHub.Domain.Content;
using ProductHub.Infrastructure.Persistence;

namespace ProductHub.Infrastructure.Content;

internal sealed class GenerationAuditReader(ProductHubDbContext db) : IGenerationAuditReader
{
    public async Task<PagedResult<GenerationAuditEntryDto>> ReadAsync(
        Guid productId,
        int page,
        int pageSize,
        string? status,
        string? language,
        CancellationToken ct
    )
    {
        var errors = new Dictionary<string, string[]>();
        var statuses = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["all"] = null,
            ["failed"] = "Failed",
            ["succeeded"] = "Succeeded",
            ["cancelled"] = "Cancelled",
            ["inprogress"] = "InProgress",
            ["queued"] = "Queued",
        };
        if (!statuses.TryGetValue(status?.Trim() ?? "all", out var filter))
            errors["status"] = ["Unsupported audit status."];
        language = language?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(language) || language == "all")
            language = null;
        if (language is not (null or "de" or "en"))
            errors["language"] = ["Language must be de, en or all."];
        if (page < 1)
            errors["page"] = ["Page must be positive."];
        if (pageSize is < 1 or > 100)
            errors["pageSize"] = ["Page size must be between 1 and 100."];
        if (errors.Count > 0)
            throw new ValidationException(errors);
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct))
            throw new NotFoundException("Product was not found.");
        // Primitive, parameterized UNION keeps server-side pagination across generation records
        // and jobs that failed/cancelled before a generation record existed. A matched job is one attempt.
        var query = db.Database.SqlQuery<AuditKey>(
            $"""
            SELECT g.Id, g.StartedAt, CAST(1 AS bit) AS HasGeneration
            FROM AiGenerations AS g
            LEFT JOIN GenerationJobs AS j ON j.ProductId = g.ProductId AND j.IdempotencyKey = g.IdempotencyKey
            WHERE g.ProductId = {productId}
              AND ({language} IS NULL OR g.Language = {language})
              AND ({filter} IS NULL OR COALESCE(CASE WHEN j.Status = N'Running' THEN N'InProgress' ELSE j.Status END, CASE WHEN g.ErrorCode = N'cancelled' THEN N'Cancelled' ELSE g.Status END) = {filter})
            UNION ALL
            SELECT j.Id, j.CreatedAt AS StartedAt, CAST(0 AS bit) AS HasGeneration
            FROM GenerationJobs AS j
            WHERE j.ProductId = {productId}
              AND NOT EXISTS (SELECT 1 FROM AiGenerations AS g WHERE g.ProductId = j.ProductId AND g.IdempotencyKey = j.IdempotencyKey)
              AND ({language} IS NULL OR JSON_VALUE(j.CommandJson, '$.Language') = {language})
              AND ({filter} IS NULL OR CASE WHEN j.Status = N'Running' THEN N'InProgress' ELSE j.Status END = {filter})
            """
        );
        var total = await query.CountAsync(ct);
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var keys = await query
            .OrderByDescending(k => k.StartedAt)
            .ThenBy(k => k.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(ct);
        if (keys.Length == 0)
            return new([], page, pageSize, total);
        var generationIds = keys.Where(k => k.HasGeneration).Select(k => k.Id).ToArray();
        var orphanIds = keys.Where(k => !k.HasGeneration).Select(k => k.Id).ToArray();
        var generations = await db
            .AiGenerations.AsNoTracking()
            .Where(g => g.ProductId == productId && generationIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, ct);
        var requestKeys = generations.Values.Select(g => g.IdempotencyKey).ToArray();
        var jobs = await db
            .GenerationJobs.AsNoTracking()
            .Where(j =>
                j.ProductId == productId
                && (orphanIds.Contains(j.Id) || requestKeys.Contains(j.IdempotencyKey))
            )
            .ToArrayAsync(ct);
        var jobsByKey = jobs.ToDictionary(j => j.IdempotencyKey, StringComparer.OrdinalIgnoreCase);
        var jobsById = jobs.ToDictionary(j => j.Id);
        var drafts = await db
            .ContentDrafts.AsNoTracking()
            .Where(d => d.ProductId == productId && generationIds.Contains(d.GenerationId))
            .Select(d => new { d.Id, d.GenerationId })
            .ToDictionaryAsync(d => d.GenerationId, d => d.Id, ct);
        var entries = new List<GenerationAuditEntryDto>();
        foreach (var key in keys)
        {
            generations.TryGetValue(key.Id, out var generation);
            GenerationJob? job;
            if (generation is not null)
                jobsByKey.TryGetValue(generation.IdempotencyKey, out job);
            else
                jobsById.TryGetValue(key.Id, out job);
            if (generation is null && job is null)
                continue;
            GenerateDraftCommand? command = null;
            if (job is not null)
            {
                try
                {
                    command = JsonSerializer.Deserialize<GenerateDraftCommand>(job.CommandJson);
                }
                catch (JsonException) { }
            }
            var metadata = generation is null ? null : GenerationMetadataMapper.Map(generation);
            var state =
                job?.Status == "Running"
                    ? "InProgress"
                    : job?.Status
                        ?? (generation?.ErrorCode == "cancelled" ? "Cancelled" : metadata!.Status);
            var terminal = state is "Succeeded" or "Failed" or "Cancelled";
            var code = job?.ErrorCode ?? metadata?.ErrorCode;
            Guid? draftId =
                generation is not null && drafts.TryGetValue(generation.Id, out var saved)
                    ? saved
                    : job?.DraftId;
            var mode = metadata?.RetrievalMode ?? CommandMode(command);
            entries.Add(
                new(
                    job?.Id ?? generation!.Id,
                    generation?.Id,
                    job?.Id,
                    draftId,
                    generation?.Language ?? command?.Language ?? "unknown",
                    generation?.Model ?? command?.Model,
                    mode,
                    generation?.IncludeFacts ?? command?.IncludeFacts,
                    generation?.RequestedTopK
                        ?? (mode is "None" or "Unknown" ? null : command?.TopK),
                    state,
                    job?.Stage ?? (state == "Succeeded" ? "Ready" : state),
                    generation?.StartedAt ?? job!.CreatedAt,
                    job is not null
                        ? terminal
                            ? job.UpdatedAt
                            : null
                        : generation?.CompletedAt,
                    generation?.DurationMs
                        ?? (
                            terminal && job is not null
                                ? Math.Max(
                                    0,
                                    (long)(job.UpdatedAt - job.CreatedAt).TotalMilliseconds
                                )
                                : null
                        ),
                    code,
                    job?.ErrorMessage ?? Describe(code),
                    metadata
                )
            );
        }
        return new(entries, page, pageSize, total);
    }

    private static string CommandMode(GenerateDraftCommand? command) =>
        command?.RetrievalMode?.Trim().ToLowerInvariant() switch
        {
            "graph" => "Graph",
            "advanced" => "Advanced",
            "naive" => "Naive",
            "none" => "None",
            _ => command is null ? "Unknown"
            : command.UseRag ? "Naive"
            : "None",
        };

    private static string? Describe(string? code) =>
        code switch
        {
            null => null,
            "timeout" => "Ollama generation timed out.",
            "generation-failed" => "Ollama could not complete the generation request.",
            "ollama-unavailable" => "Ollama or the configured model could not be reached.",
            "invalid-json" => "Ollama returned invalid JSON.",
            "invalid-output" => "The model returned incomplete or invalid content.",
            "cancelled" => "Generation was cancelled.",
            "rag-no-context" => "No completed product knowledge matched this request.",
            "rag-retrieval-failed" => "Knowledge retrieval failed before generation completed.",
            "rag-not-configured" => "Knowledge retrieval is not configured.",
            _ => "Generation failed. Inspect the error code and server trace for details.",
        };

    private sealed class AuditKey
    {
        public Guid Id { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public bool HasGeneration { get; set; }
    }
}
