using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ProductHub.Application.Abstractions;
using ProductHub.Application.Content;
using ProductHub.Domain.Content;
using ProductHub.Domain.Products;

namespace ProductHub.Infrastructure.Persistence;

internal sealed class ContentGenerationStore(ProductHubDbContext dbContext)
    : IContentGenerationStore
{
    public Task<Product?> GetProductAsync(Guid productId, CancellationToken cancellationToken) =>
        dbContext
            .Products.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == productId, cancellationToken);

    public Task<AiGeneration?> GetGenerationAsync(
        Guid productId,
        string idempotencyKey,
        CancellationToken cancellationToken
    ) =>
        dbContext
            .AiGenerations.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ProductId == productId && x.IdempotencyKey == idempotencyKey,
                cancellationToken
            );

    public Task<ContentDraft?> GetDraftByGenerationAsync(
        Guid generationId,
        CancellationToken cancellationToken
    ) =>
        dbContext
            .ContentDrafts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.GenerationId == generationId, cancellationToken);

    public Task<ContentDraft?> GetDraftAsync(Guid draftId, CancellationToken cancellationToken) =>
        dbContext
            .ContentDrafts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == draftId, cancellationToken);

    public Task<ContentDraft?> GetDraftForUpdateAsync(
        Guid draftId,
        CancellationToken cancellationToken
    ) => dbContext.ContentDrafts.SingleOrDefaultAsync(x => x.Id == draftId, cancellationToken);

    public async Task<IReadOnlyList<ContentDraft>> ListDraftsForUpdateAsync(
        Guid productId,
        string language,
        CancellationToken cancellationToken
    ) =>
        await dbContext
            .ContentDrafts.Where(x => x.ProductId == productId && x.Language == language)
            .ToListAsync(cancellationToken);

    public Task<AiGeneration?> GetGenerationByIdAsync(
        Guid generationId,
        CancellationToken cancellationToken
    ) =>
        dbContext
            .AiGenerations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == generationId, cancellationToken);

    public async Task<IReadOnlyList<ContentDraft>> ListDraftsAsync(
        Guid productId,
        CancellationToken cancellationToken
    ) =>
        await dbContext
            .ContentDrafts.AsNoTracking()
            .Where(x => x.ProductId == productId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task AddGenerationAsync(AiGeneration generation, CancellationToken cancellationToken) =>
        dbContext.AiGenerations.AddAsync(generation, cancellationToken).AsTask();

    public Task AddDraftAsync(ContentDraft draft, CancellationToken cancellationToken) =>
        dbContext.ContentDrafts.AddAsync(draft, cancellationToken).AsTask();

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new DuplicateIdempotencyKeyException(ex);
        }
    }
}
