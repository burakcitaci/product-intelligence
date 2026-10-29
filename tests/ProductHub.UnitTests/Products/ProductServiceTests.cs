using ProductHub.Application.Abstractions;
using ProductHub.Application.Products;
using ProductHub.Domain.Products;

namespace ProductHub.UnitTests.Products;

public sealed class ProductServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsValidProduct()
    {
        var repository = new FakeRepository();
        var result = await new ProductService(repository).CreateAsync(
            new CreateProductCommand("sku-1", "Widget", "Acme", null, null, []),
            CancellationToken.None
        );
        Assert.Equal("SKU-1", result.Sku);
        Assert.True(repository.Saved);
    }

    [Fact]
    public async Task CreateAsync_RejectsExistingSku()
    {
        var repository = new FakeRepository { ExistingSku = true };
        await Assert.ThrowsAsync<ConflictException>(() =>
            new ProductService(repository).CreateAsync(
                new CreateProductCommand("sku-1", "Widget", "Acme", null, null, []),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task ListAsync_RejectsUnboundedPageSize() =>
        await Assert.ThrowsAsync<ValidationException>(() =>
            new ProductService(new FakeRepository()).ListAsync(1, 101, CancellationToken.None)
        );

    [Fact]
    public async Task UpdateAsync_ReplacesSourceFacts()
    {
        var product = Product.Create("sku-1", "Widget", "Acme");
        var repository = new FakeRepository();
        repository.Products.Add(product);
        var result = await new ProductService(repository).UpdateAsync(
            new UpdateProductCommand(
                product.Id,
                "Updated widget",
                "Acme",
                null,
                [new ProductAttributeData("Weight", "2", "kg")],
                Convert.ToBase64String([1])
            ),
            CancellationToken.None
        );
        Assert.Equal("Updated widget", result.Name);
        Assert.Single(result.Attributes);
    }

    private sealed class FakeRepository : IProductRepository
    {
        public bool ExistingSku { get; init; }
        public bool Saved { get; private set; }
        public List<Product> Products { get; } = [];

        public Task AddAsync(Product product, CancellationToken cancellationToken)
        {
            Products.Add(product);
            return Task.CompletedTask;
        }

        public Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Products.SingleOrDefault(x => x.Id == id));

        public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Products.SingleOrDefault(x => x.Id == id));

        public void SetOriginalRowVersion(Product product, byte[] rowVersion) { }

        public Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingSku);

        public Task<(IReadOnlyList<Product> Items, int TotalCount)> ListAsync(
            int page,
            int pageSize,
            Guid? catalogId,
            CancellationToken cancellationToken
        ) => Task.FromResult(((IReadOnlyList<Product>)Products, Products.Count));

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saved = true;
            return Task.CompletedTask;
        }
    }
}
