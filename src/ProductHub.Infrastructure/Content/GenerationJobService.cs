using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductHub.Application.Content;
using ProductHub.Domain.Content;
using ProductHub.Infrastructure.Persistence;

namespace ProductHub.Infrastructure.Content;

public sealed class GenerationJobService(
    ProductHubDbContext db,
    IOptions<AiSettings> settings,
    ILogger<GenerationJobService> logger
)
    : IGenerationJobService
{
    public async Task<GenerationJobDto> EnqueueAsync(
        GenerateDraftCommand command,
        string requestedBy,
        CancellationToken ct
    )
    {
        ContentGenerationService.Validate(command, settings.Value);
        command = command with { IdempotencyKey = command.IdempotencyKey.Trim() };
        if (!await db.Products.AnyAsync(p => p.Id == command.ProductId, ct))
            throw new NotFoundException("Product was not found.");
        var existing = await db
            .GenerationJobs.AsNoTracking()
            .SingleOrDefaultAsync(
                j => j.ProductId == command.ProductId && j.IdempotencyKey == command.IdempotencyKey,
                ct
            );
        if (existing is not null)
            {
                logger.LogDebug(
                    "Reusing generation job {JobId} for product {ProductId}.",
                    existing.Id,
                    command.ProductId
                );
                return Map(existing);
            }
        var normalized = command with
        {
            Model = string.IsNullOrWhiteSpace(command.Model)
                ? settings.Value.Model
                : command.Model.Trim(),
        };
        var job = GenerationJob.Queue(
            command.ProductId,
            command.IdempotencyKey,
            JsonSerializer.Serialize(normalized),
            requestedBy
        );
        db.GenerationJobs.Add(job);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(job).State = EntityState.Detached;
            existing = await db
                .GenerationJobs.AsNoTracking()
                .SingleOrDefaultAsync(
                    j =>
                        j.ProductId == command.ProductId
                        && j.IdempotencyKey == command.IdempotencyKey,
                    ct
                );
            if (existing is null)
                throw;
            logger.LogDebug(
                "Reusing generation job {JobId} after a concurrent enqueue for product {ProductId}.",
                existing.Id,
                command.ProductId
            );
            return Map(existing);
        }
        logger.LogInformation(
            "Queued generation job {JobId} for product {ProductId}.",
            job.Id,
            command.ProductId
        );
        return Map(job);
    }

    public async Task<GenerationJobDto> GetAsync(Guid id, CancellationToken ct) =>
        Map(
            await db.GenerationJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, ct)
                ?? throw new NotFoundException("Generation job was not found.")
        );

    public async Task<IReadOnlyList<GenerationJobDto>> ListAsync(
        Guid productId,
        CancellationToken ct
    ) =>
        (
            await db
                .GenerationJobs.AsNoTracking()
                .Where(j => j.ProductId == productId)
                .OrderByDescending(j => j.CreatedAt)
                .Take(50)
                .ToArrayAsync(ct)
        )
            .Select(Map)
            .ToArray();

    public async Task<GenerationJobDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db
            .GenerationJobs.Where(j =>
                j.Id == id && (j.Status == "Queued" || j.Status == "Running")
            )
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(j => j.CancellationRequested, true)
                        .SetProperty(
                            j => j.Status,
                            j => j.Status == "Queued" ? "Cancelled" : j.Status
                        )
                        .SetProperty(
                            j => j.Stage,
                            j => j.Status == "Queued" ? "Cancelled" : j.Stage
                        )
                        .SetProperty(j => j.UpdatedAt, now),
                ct
            );
        return await GetAsync(id, ct);
    }

    internal static GenerationJobDto Map(GenerationJob j) =>
        new(
            j.Id,
            j.ProductId,
            j.Status,
            j.Stage,
            j.CreatedAt,
            j.UpdatedAt,
            j.CancellationRequested,
            j.DraftId,
            j.ErrorCode,
            j.ErrorMessage,
            j.IdempotencyKey
        );
}
