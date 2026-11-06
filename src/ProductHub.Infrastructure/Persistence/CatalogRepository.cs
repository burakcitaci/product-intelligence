using Microsoft.EntityFrameworkCore;
using ProductHub.Application.Abstractions;
using ProductHub.Application.Catalogs;
using ProductHub.Application.Products;
using ProductHub.Domain.Catalogs;
using ProductHub.Domain.Content;

namespace ProductHub.Infrastructure.Persistence;

internal sealed class CatalogRepository(ProductHubDbContext dbContext) : ICatalogRepository
{
    public Task AddAsync(Catalog catalog, CancellationToken cancellationToken) =>
        dbContext.Catalogs.AddAsync(catalog, cancellationToken).AsTask();

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Catalogs.AnyAsync(x => x.Id == id, cancellationToken);

    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
        dbContext.Catalogs.AnyAsync(x => x.Name == name, cancellationToken);

    public async Task<IReadOnlyList<(Catalog Catalog, int ProductCount)>> ListAsync(
        CancellationToken cancellationToken
    )
    {
        var rows = await dbContext
            .Catalogs.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                Catalog = x,
                Count = dbContext.Products.Count(p => p.CatalogId == x.Id),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(x => (x.Catalog, x.Count)).ToArray();
    }

    public async Task<ReviewQueueResult> GetReviewQueueAsync(
        Guid catalogId,
        string language,
        string filter,
        string search,
        int page,
        int pageSize,
        CancellationToken ct
    )
    {
        var query = dbContext.Products.AsNoTracking().Where(p => p.CatalogId == catalogId);
        if (search.Length > 0)
        {
            var term = search.ToLowerInvariant();
            query = query.Where(p =>
                p.Name.ToLower().Contains(term)
                || p.Sku.ToLower().Contains(term)
                || p.Brand.ToLower().Contains(term)
            );
        }
        // Counts cover the complete matching catalog, not a pre-truncated product page.
        // Converted JSON attributes require hydration for the missing-facts count.
        var products = await query.OrderBy(p => p.Sku).ThenBy(p => p.Id).ToArrayAsync(ct);
        var summaries = await dbContext
            .ContentDrafts.AsNoTracking()
            .Join(query, d => d.ProductId, p => p.Id, (d, p) => d)
            .Join(
                dbContext.AiGenerations,
                d => d.GenerationId,
                g => g.Id,
                (d, g) => new { Draft = d, Generation = g }
            )
            .Where(x => x.Draft.Language == language)
            .Select(x => new
            {
                x.Draft.ProductId,
                x.Draft.Status,
                x.Draft.IsCurrent,
                x.Generation.StartedAt,
            })
            .ToArrayAsync(ct);
        var byProduct = summaries.ToLookup(d => d.ProductId);
        var rows = products
            .Select(p =>
            {
                var drafts = byProduct[p.Id].ToArray();
                var current = drafts
                    .Where(d => d.IsCurrent && d.Status == ContentDraftStatus.Approved)
                    .ToArray();
                var dto = new ProductDto(
                    p.Id,
                    p.Sku,
                    p.Name,
                    p.Brand,
                    p.CatalogId,
                    p.CategoryId,
                    p.Attributes.Select(a => new ProductAttributeData(a.Name, a.Value, a.Unit))
                        .ToArray(),
                    p.CreatedAt,
                    p.UpdatedAt,
                    Convert.ToBase64String(p.RowVersion)
                );
                return new ReviewQueueRow(
                    dto,
                    drafts.Count(d => d.Status == ContentDraftStatus.PendingReview),
                    current.Length == 0,
                    p.Attributes.Count == 0,
                    current.Any(d => p.UpdatedAt > d.StartedAt)
                );
            })
            .ToArray();
        return ReviewQueue.Select(rows, filter, page, pageSize);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
