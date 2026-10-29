using ProductHub.Domain.Products;

namespace ProductHub.Application.Abstractions;

public interface IProductRepository
{
    Task AddAsync(Product product, CancellationToken cancellationToken);
    Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Product?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);
    void SetOriginalRowVersion(Product product, byte[] rowVersion);
    Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken);
    Task<(IReadOnlyList<Product> Items, int TotalCount)> ListAsync(
        int page,
        int pageSize,
        Guid? catalogId,
        CancellationToken cancellationToken
    );
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
