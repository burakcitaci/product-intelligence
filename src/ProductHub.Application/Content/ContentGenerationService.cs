using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductHub.Application.Abstractions;
using ProductHub.Application.Knowledge;
using ProductHub.Application.Products;
using ProductHub.Domain.Content;

namespace ProductHub.Application.Content;

public sealed class ContentGenerationService(
    IContentGenerationStore store,
    IContentGenerator generator,
    IOptions<AiSettings> settings,
    ILogger<ContentGenerationService> logger,
    KnowledgeSearchService? knowledgeSearch = null
)
{
    public async Task<ContentDraftDto> GenerateAsync(
        GenerateDraftCommand command,
        CancellationToken cancellationToken,
        Func<string, CancellationToken, Task>? progress = null
    )
    {
        var options = settings.Value;
        Validate(command, options);
        command = command with { IdempotencyKey = command.IdempotencyKey.Trim() };
        var retrievalMode = ResolveRetrievalMode(command);
        var useRag = retrievalMode != RetrievalMode.None;
        var product =
            await store.GetProductAsync(command.ProductId, cancellationToken)
            ?? throw new NotFoundException($"Product '{command.ProductId}' was not found.");

        if (
            await store.GetGenerationAsync(
                command.ProductId,
                command.IdempotencyKey,
                cancellationToken
            ) is
            { } existing
        )
        {
            var existingDraft = await store.GetDraftByGenerationAsync(
                existing.Id,
                cancellationToken
            );
            if (existingDraft is not null)
                return Map(existingDraft);
            throw new ConflictException(
                existing.Status == AiGenerationStatus.InProgress
                    ? "A generation with this idempotency key is already in progress."
                    : "This idempotency key was already used by a failed generation. Use a new key to retry."
            );
        }

        var selectedModel = string.IsNullOrWhiteSpace(command.Model)
            ? options.Model
            : command.Model.Trim();
        var generation = AiGeneration.Start(
            product.Id,
            command.IdempotencyKey,
            options.Provider,
            selectedModel,
            options.PromptVersion,
            NormalizeLanguage(command.Language),
            retrievalMode switch
            {
                RetrievalMode.Graph => GroundingMode.GraphRag,
                RetrievalMode.Advanced => GroundingMode.AdvancedRag,
                RetrievalMode.Naive => GroundingMode.Rag,
                _ => command.IncludeFacts ? GroundingMode.FactsOnly : GroundingMode.IdentityOnly,
            },
            command.CorrelationId,
            useRag ? command.TopK : null,
            includeFacts: command.IncludeFacts
        );
        generation.RecordParameters(
            JsonSerializer.Serialize(
                new GenerationParametersDto(
                    GeneratedContentContract.Version,
                    options.Temperature,
                    options.Seed,
                    options.MaxOutputTokens
                )
            )
        );
        await store.AddGenerationAsync(generation, cancellationToken);
        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            throw new ConflictException("A generation with this idempotency key already exists.");
        }

        try
        {
            if (progress is not null)
                await progress("Retrieving", cancellationToken);
            IReadOnlyList<KnowledgeSearchResult> knowledge = [];
            if (useRag)
            {
                if (knowledgeSearch is null)
                {
                    generation.RecordRetrievalFailure(0, "rag-not-configured");
                    throw new ContentGenerationException(
                        "rag-not-configured",
                        "Knowledge retrieval is not configured."
                    );
                }
                var retrieval = Stopwatch.StartNew();
                try
                {
                    var query = string.Join(
                        " | ",
                        new[] { product.Sku, product.Name, product.Brand }.Concat(
                            command.IncludeFacts
                                ? product.Attributes.Select(x => $"{x.Name}: {x.Value} {x.Unit}")
                                : []
                        )
                    );
                    knowledge = await knowledgeSearch.SearchAsync(
                        new SearchKnowledgeQuery(
                            query[..Math.Min(query.Length, 2000)],
                            command.TopK,
                            product.Id,
                            retrievalMode
                        ),
                        cancellationToken
                    );
                    retrieval.Stop();
                    if (knowledge.Count == 0)
                    {
                        generation.RecordRetrievalFailure(
                            retrieval.ElapsedMilliseconds,
                            "rag-no-context"
                        );
                        throw new ContentGenerationException(
                            "rag-no-context",
                            "No completed knowledge document matched this product. Upload product knowledge or generate with facts only."
                        );
                    }
                    var sources = knowledge
                        .Select(x => new RetrievalSourceDto(
                            x.DocumentId,
                            x.FileName,
                            x.Citation,
                            x.Score,
                            x.GraphHops,
                            x.GraphPath,
                            x.DocumentRevision,
                            x.ChunkRevision,
                            x.RetrievalVersion,
                            x.DenseCandidateCount,
                            x.LexicalCandidateCount,
                            x.GraphRevision,
                            x.GraphCacheHit,
                            x.GraphConcepts
                        ))
                        .ToArray();
                    generation.RecordRetrieval(
                        retrieval.ElapsedMilliseconds,
                        knowledge.Count,
                        knowledge.Select(x => x.DocumentId).Distinct().Count(),
                        JsonSerializer.Serialize(sources)
                    );
                    await store.SaveChangesAsync(cancellationToken);
                }
                catch (ContentGenerationException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    retrieval.Stop();
                    generation.RecordRetrievalFailure(
                        retrieval.ElapsedMilliseconds,
                        "rag-retrieval-failed"
                    );
                    throw new ContentGenerationException(
                        "rag-retrieval-failed",
                        "Knowledge retrieval failed; the draft was not generated.",
                        exception
                    );
                }
            }
            var context = new ProductContentContext(
                product.Sku,
                product.Name,
                product.Brand,
                command.IncludeFacts
                    ? product
                        .Attributes.Select(x => new ProductFact(x.Name, x.Value, x.Unit))
                        .ToArray()
                    : [],
                knowledge
                    .Select(x => new GroundingExcerpt(
                        x.Citation,
                        x.Text,
                        x.DocumentId,
                        x.ChunkId,
                        x.DocumentRevision,
                        x.ChunkRevision
                    ))
                    .ToArray(),
                command.IncludeFacts
            );
            if (progress is not null)
                await progress("Generating", cancellationToken);
            var generated = await generator.GenerateAsync(
                context,
                new GenerationOptions(
                    generation.Language,
                    generation.PromptVersion,
                    generation.Model,
                    options.Temperature,
                    options.Seed,
                    options.MaxOutputTokens
                ),
                cancellationToken
            );
            if (progress is not null)
                await progress("Validating", cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateGenerated(generated);

            var draft = ContentDraft.Create(
                product.Id,
                generation.Language,
                generated.Title,
                generated.ShortDescription,
                generated.LongDescription,
                generated.Highlights,
                generation.Id
            );
            draft.RecordClaimReview(
                JsonSerializer.Serialize(ClaimReview.Assess(generated, context))
            );
            generation.Complete(generated.InputTokens, generated.OutputTokens);
            await store.AddDraftAsync(draft, cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            return Map(draft);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await PersistFailureAsync(generation, "cancelled");
            throw;
        }
        catch (ContentGenerationException exception)
        {
            await PersistFailureAsync(generation, exception.ErrorCode);
            throw new GenerationUnavailableException(
                exception.ErrorCode,
                exception.Message,
                exception
            );
        }
        catch (ArgumentException exception)
        {
            await PersistFailureAsync(generation, "invalid-output");
            throw new GenerationUnavailableException(
                "invalid-output",
                "The model returned invalid product content.",
                exception
            );
        }
    }

    public async Task<ContentDraftDto> GetAsync(
        Guid draftId,
        CancellationToken cancellationToken
    ) =>
        (await store.GetDraftAsync(draftId, cancellationToken)) is { } draft
            ? Map(draft)
            : throw new NotFoundException($"Draft '{draftId}' was not found.");

    public async Task<IReadOnlyList<ContentDraftDto>> ListAsync(
        Guid productId,
        CancellationToken cancellationToken
    )
    {
        if (await store.GetProductAsync(productId, cancellationToken) is null)
            throw new NotFoundException($"Product '{productId}' was not found.");
        return (await store.ListDraftsAsync(productId, cancellationToken)).Select(Map).ToArray();
    }

    public async Task<ContentDraftDto> ReviewAsync(
        Guid draftId,
        bool approve,
        CancellationToken cancellationToken,
        bool acknowledgeWarnings = false,
        string? reviewerId = null
    )
    {
        var draft =
            await store.GetDraftForUpdateAsync(draftId, cancellationToken)
            ?? throw new NotFoundException($"Draft '{draftId}' was not found.");
        try
        {
            if (approve)
            {
                if (draft.ClaimReviewJson is not null)
                {
                    var review = JsonSerializer.Deserialize<ClaimReviewReport>(
                        draft.ClaimReviewJson
                    )!;
                    if (review.RequiresAcknowledgement)
                    {
                        if (!acknowledgeWarnings || string.IsNullOrWhiteSpace(reviewerId))
                            throw new ConflictException(
                                "Inspect the claim evidence and explicitly acknowledge review warnings before approval."
                            );
                        draft.RecordClaimReview(
                            JsonSerializer.Serialize(
                                review with
                                {
                                    WarningsAcknowledged = true,
                                    AcknowledgedBy = reviewerId,
                                    AcknowledgedAt = DateTimeOffset.UtcNow,
                                }
                            )
                        );
                    }
                }
                draft.Approve();
                var siblings = await store.ListDraftsForUpdateAsync(
                    draft.ProductId,
                    draft.Language,
                    cancellationToken
                );
                if (!siblings.Any(x => x.IsCurrent))
                    draft.MarkCurrent();
            }
            else
                draft.Reject();
        }
        catch (InvalidOperationException)
        {
            throw new ConflictException("Draft cannot be reviewed from its current status.");
        }
        await store.SaveChangesAsync(cancellationToken);
        return Map(draft);
    }

    public async Task<ContentDraftDto> SetCurrentAsync(
        Guid draftId,
        CancellationToken cancellationToken
    )
    {
        var draft =
            await store.GetDraftForUpdateAsync(draftId, cancellationToken)
            ?? throw new NotFoundException($"Draft '{draftId}' was not found.");
        if (draft.Status != ContentDraftStatus.Approved)
            throw new ConflictException("Only approved drafts can be made current.");

        if (draft.ClaimReviewJson is not null)
        {
            var review = JsonSerializer.Deserialize<ClaimReviewReport>(draft.ClaimReviewJson)!;
            if (review.RequiresAcknowledgement && !review.WarningsAcknowledged)
                throw new ConflictException(
                    "Review warnings must be acknowledged before publishing."
                );
        }
        var siblings = await store.ListDraftsForUpdateAsync(
            draft.ProductId,
            draft.Language,
            cancellationToken
        );
        if (draft.IsCurrent)
            return Map(draft);
        foreach (var sibling in siblings.Where(x => x.IsCurrent))
            sibling.UnmarkCurrent();
        await store.SaveChangesAsync(cancellationToken);
        draft.MarkCurrent();
        await store.SaveChangesAsync(cancellationToken);
        return Map(draft);
    }

    public async Task<ContentDraftDto> UpdateDraftAsync(
        Guid draftId,
        UpdateDraftCommand command,
        CancellationToken cancellationToken
    )
    {
        var errors = ValidateDraftUpdate(command);
        if (errors.Count > 0)
            throw new ValidationException(errors);
        var draft =
            await store.GetDraftForUpdateAsync(draftId, cancellationToken)
            ?? throw new NotFoundException($"Draft '{draftId}' was not found.");
        try
        {
            draft.UpdateContent(
                command.Title,
                command.ShortDescription,
                command.LongDescription,
                command.Highlights
            );
        }
        catch (InvalidOperationException)
        {
            throw new ConflictException("Only pending drafts can be edited.");
        }
        if (draft.ClaimReviewJson is not null)
        {
            var original = JsonSerializer.Deserialize<ClaimReviewReport>(draft.ClaimReviewJson)!;
            draft.RecordClaimReview(
                JsonSerializer.Serialize(
                    ClaimReview.Assess(
                        new(
                            draft.Title,
                            draft.ShortDescription,
                            draft.LongDescription,
                            draft.Highlights
                        ),
                        original.Context
                    )
                )
            );
        }
        await store.SaveChangesAsync(cancellationToken);
        return Map(draft);
    }

    public async Task<AiGenerationMetadataDto> GetGenerationMetadataAsync(
        Guid draftId,
        CancellationToken cancellationToken
    )
    {
        var draft =
            await store.GetDraftAsync(draftId, cancellationToken)
            ?? throw new NotFoundException($"Draft '{draftId}' was not found.");
        var generation =
            await store.GetGenerationByIdAsync(draft.GenerationId, cancellationToken)
            ?? throw new NotFoundException($"Generation '{draft.GenerationId}' was not found.");
        return GenerationMetadataMapper.Map(generation);
    }

    private async Task PersistFailureAsync(AiGeneration generation, string errorCode)
    {
        generation.Fail(errorCode);
        try
        {
            await store.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to persist generation failure {GenerationId} with code {ErrorCode}.",
                generation.Id,
                errorCode
            );
        }
    }

    public static void Validate(GenerateDraftCommand command, AiSettings settings)
    {
        var errors = new Dictionary<string, string[]>();
        if (command.ProductId == Guid.Empty)
            errors["productId"] = ["Product id is required."];
        if (
            string.IsNullOrWhiteSpace(command.IdempotencyKey)
            || command.IdempotencyKey.Trim().Length > 100
        )
            errors["idempotencyKey"] =
            [
                "Idempotency-Key is required and must not exceed 100 characters.",
            ];
        if (command.Language?.Trim().ToLowerInvariant() is not ("de" or "en"))
            errors["language"] = ["Language must be 'de' or 'en'."];
        if (
            command.RetrievalMode is not null
            && !new[] { "none", "naive", "advanced", "graph" }.Contains(
                command.RetrievalMode.Trim().ToLowerInvariant()
            )
        )
            errors["retrievalMode"] = ["Retrieval mode must be None, Naive, Advanced, or Graph."];
        if (ResolveRetrievalMode(command) != RetrievalMode.None && command.TopK is < 1 or > 20)
            errors["topK"] = ["Top-K must be between 1 and 20."];
        var selectedModel = string.IsNullOrWhiteSpace(command.Model)
            ? settings.Model
            : command.Model.Trim();
        if (!settings.Models.Contains(selectedModel, StringComparer.OrdinalIgnoreCase))
            errors["model"] = ["Select a supported AI model."];
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static RetrievalMode ResolveRetrievalMode(GenerateDraftCommand command) =>
        command.RetrievalMode?.Trim().ToLowerInvariant() switch
        {
            "graph" => RetrievalMode.Graph,
            "advanced" => RetrievalMode.Advanced,
            "naive" => RetrievalMode.Naive,
            "none" => RetrievalMode.None,
            _ => command.UseRag ? RetrievalMode.Naive : RetrievalMode.None,
        };

    private static void ValidateGenerated(GeneratedProductContent content) =>
        GeneratedContentContract.Validate(content);

    private static Dictionary<string, string[]> ValidateDraftUpdate(UpdateDraftCommand command)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(command.Title) || command.Title.Trim().Length > 200)
            errors["title"] = ["Title is required and must not exceed 200 characters."];
        if (
            string.IsNullOrWhiteSpace(command.ShortDescription)
            || command.ShortDescription.Trim().Length > 500
        )
            errors["shortDescription"] =
            [
                "Short description is required and must not exceed 500 characters.",
            ];
        if (
            string.IsNullOrWhiteSpace(command.LongDescription)
            || command.LongDescription.Trim().Length > 8000
        )
            errors["longDescription"] =
            [
                "Long description is required and must not exceed 8000 characters.",
            ];
        if (
            command.Highlights is null
            || command.Highlights.Count > 5
            || command.Highlights.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > 300)
        )
            errors["highlights"] =
            [
                "At most five highlights are allowed; supplied highlights must be non-empty and at most 300 characters.",
            ];
        return errors;
    }

    private static string NormalizeLanguage(string language) => language.Trim().ToLowerInvariant();

    private static ContentDraftDto Map(ContentDraft draft) =>
        new(
            draft.Id,
            draft.ProductId,
            draft.Language,
            draft.Title,
            draft.ShortDescription,
            draft.LongDescription,
            draft.Highlights.ToArray(),
            draft.Status,
            draft.IsCurrent,
            draft.CreatedAt,
            draft.GenerationId,
            draft.ClaimReviewJson is null
                ? null
                : JsonSerializer.Deserialize<ClaimReviewReport>(draft.ClaimReviewJson)
        );
}

public sealed class DuplicateIdempotencyKeyException(Exception innerException)
    : Exception("Duplicate idempotency key.", innerException);
