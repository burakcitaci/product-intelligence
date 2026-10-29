using Microsoft.EntityFrameworkCore;
using ProductHub.Application.Abstractions;
using ProductHub.Domain.Catalogs;

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

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
