using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ProductHub.Application.Abstractions;
using ProductHub.Application.Products;
using ProductHub.Domain.Products;

namespace ProductHub.Infrastructure.Persistence;

internal sealed class ProductRepository(ProductHubDbContext dbContext) : IProductRepository
{
    public Task AddAsync(Product product, CancellationToken cancellationToken) =>
        dbContext.Products.AddAsync(product, cancellationToken).AsTask();

    public Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Products.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void SetOriginalRowVersion(Product product, byte[] rowVersion) =>
        dbContext.Entry(product).Property(x => x.RowVersion).OriginalValue = rowVersion;

    public Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken) =>
        dbContext.Products.AnyAsync(x => x.Sku == sku, cancellationToken);

    public async Task<(IReadOnlyList<Product> Items, int TotalCount)> ListAsync(
        int page,
        int pageSize,
        Guid? catalogId,
        CancellationToken cancellationToken
    )
    {
        var query = dbContext
            .Products.AsNoTracking()
            .Where(x => catalogId == null || x.CatalogId == catalogId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.Sku)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new DuplicateSkuException(ex);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ProductConcurrencyException(ex);
        }
    }
}
