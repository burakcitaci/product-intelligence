using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductHub.Application.Abstractions;
using ProductHub.Application.Products;
using ProductHub.Domain.Content;

namespace ProductHub.Application.Content;

public sealed class ContentGenerationService(
    IContentGenerationStore store,
    IContentGenerator generator,
    IOptions<AiSettings> settings,
    ILogger<ContentGenerationService> logger
)
{
    public async Task<ContentDraftDto> GenerateAsync(
        GenerateDraftCommand command,
        CancellationToken cancellationToken
    )
    {
        Validate(command);
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

        var options = settings.Value;
        var generation = AiGeneration.Start(
            product.Id,
            command.IdempotencyKey,
            options.Provider,
            options.Model,
            options.PromptVersion,
            NormalizeLanguage(command.Language),
            command.CorrelationId
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
            var context = new ProductContentContext(
                product.Sku,
                product.Name,
                product.Brand,
                product.Attributes.Select(x => new ProductFact(x.Name, x.Value, x.Unit)).ToArray()
            );
            var generated = await generator.GenerateAsync(
                context,
                new GenerationOptions(generation.Language, generation.PromptVersion),
                cancellationToken
            );
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
        CancellationToken cancellationToken
    )
    {
        var draft =
            await store.GetDraftForUpdateAsync(draftId, cancellationToken)
            ?? throw new NotFoundException($"Draft '{draftId}' was not found.");
        try
        {
            if (approve)
            {
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
        return new(
            generation.Id,
            generation.Provider,
            generation.Model,
            generation.PromptVersion,
            generation.StartedAt,
            generation.CompletedAt,
            generation.DurationMs,
            generation.InputTokens,
            generation.OutputTokens,
            generation.Status.ToString(),
            generation.ErrorCode,
            generation.CorrelationId
        );
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

    private static void Validate(GenerateDraftCommand command)
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
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static void ValidateGenerated(GeneratedProductContent content)
    {
        if (
            string.IsNullOrWhiteSpace(content.Title)
            || content.Title.Length > 200
            || string.IsNullOrWhiteSpace(content.ShortDescription)
            || content.ShortDescription.Length > 500
            || string.IsNullOrWhiteSpace(content.LongDescription)
            || content.LongDescription.Length > 8000
            || content.Highlights is null
            || content.Highlights.Count is < 3 or > 5
            || content.Highlights.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 300)
        )
            throw new ContentGenerationException(
                "invalid-output",
                "The model returned incomplete or invalid JSON content."
            );
    }

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
            || command.Highlights.Count is < 3 or > 5
            || command.Highlights.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > 300)
        )
            errors["highlights"] =
            [
                "Three to five non-empty highlights are required; each may contain up to 300 characters.",
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
            draft.GenerationId
        );
}

public sealed class DuplicateIdempotencyKeyException(Exception innerException)
    : Exception("Duplicate idempotency key.", innerException);
