using ProductHub.Application.Catalogs;
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
    Task<ReviewQueueResult> GetReviewQueueAsync(
        Guid catalogId,
        string language,
        string filter,
        string search,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    );
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
