using ProductHub.Domain.Catalogs;

namespace ProductHub.Application.Abstractions;

public interface ICatalogRepository
{
    Task AddAsync(Catalog catalog, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<(Catalog Catalog, int ProductCount)>> ListAsync(
        CancellationToken cancellationToken
    );
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
