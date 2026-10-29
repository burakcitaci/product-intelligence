using ProductHub.Domain.Content;
using ProductHub.Domain.Products;

namespace ProductHub.Application.Abstractions;

public interface IContentGenerationStore
{
    Task<Product?> GetProductAsync(Guid productId, CancellationToken cancellationToken);
    Task<AiGeneration?> GetGenerationAsync(
        Guid productId,
        string idempotencyKey,
        CancellationToken cancellationToken
    );
    Task<ContentDraft?> GetDraftByGenerationAsync(
        Guid generationId,
        CancellationToken cancellationToken
    );
    Task<ContentDraft?> GetDraftAsync(Guid draftId, CancellationToken cancellationToken);
    Task<ContentDraft?> GetDraftForUpdateAsync(Guid draftId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContentDraft>> ListDraftsForUpdateAsync(
        Guid productId,
        string language,
        CancellationToken cancellationToken
    );
    Task<AiGeneration?> GetGenerationByIdAsync(
        Guid generationId,
        CancellationToken cancellationToken
    );
    Task<IReadOnlyList<ContentDraft>> ListDraftsAsync(
        Guid productId,
        CancellationToken cancellationToken
    );
    Task AddGenerationAsync(AiGeneration generation, CancellationToken cancellationToken);
    Task AddDraftAsync(ContentDraft draft, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
