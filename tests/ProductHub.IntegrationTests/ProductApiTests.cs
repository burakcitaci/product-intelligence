using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductHub.Application.Catalogs;
using ProductHub.Application.Products;
using ProductHub.Infrastructure.Persistence;

namespace ProductHub.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class ProductApiTests(SqlServerFixture database)
{
    [Fact]
    public async Task CreateThenGet_RoundTripsProduct()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var sku = $"TEST-{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku,
                name = "Trail Bottle",
                brand = "Northwind",
                categoryId = (Guid?)null,
                attributes = new[]
                {
                    new
                    {
                        name = "Volume",
                        value = "750",
                        unit = "ml",
                    },
                },
            },
            CancellationToken.None
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ProductDto>(CancellationToken.None);
        Assert.NotNull(created);

        var stored = await client.GetFromJsonAsync<ProductDto>(
            $"/api/products/{created.Id}",
            CancellationToken.None
        );
        Assert.Equal(sku.ToUpperInvariant(), stored!.Sku);
        Assert.Single(stored.Attributes);
    }

    [Fact]
    public async Task Create_DuplicateSku_ReturnsConflict()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var request = new
        {
            sku = $"DUP-{Guid.NewGuid():N}",
            name = "Item",
            brand = "Brand",
            attributes = Array.Empty<object>(),
        };
        Assert.Equal(
            HttpStatusCode.Created,
            (
                await client.PostAsJsonAsync("/api/products", request, CancellationToken.None)
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync("/api/products", request, CancellationToken.None)
            ).StatusCode
        );
    }

    [Fact]
    public async Task Catalog_CreateProductAndFilter_RoundTripsRelationship()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var catalogResponse = await client.PostAsJsonAsync(
            "/api/catalogs",
            new { name = $"Catalog {Guid.NewGuid():N}", description = "Integration test" }
        );
        var catalog = await catalogResponse.Content.ReadFromJsonAsync<CatalogDto>();
        Assert.Equal(HttpStatusCode.Created, catalogResponse.StatusCode);

        var productResponse = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = $"CAT-{Guid.NewGuid():N}",
                name = "Catalog Product",
                brand = "Northwind",
                catalogId = catalog!.Id,
                attributes = new[]
                {
                    new
                    {
                        name = "Material",
                        value = "Steel",
                        unit = (string?)null,
                    },
                },
            }
        );
        var product = await productResponse.Content.ReadFromJsonAsync<ProductDto>();
        Assert.Equal(catalog.Id, product!.CatalogId);

        var filtered = await client.GetFromJsonAsync<PagedResult<ProductDto>>(
            $"/api/products?catalogId={catalog.Id}"
        );
        Assert.Contains(filtered!.Items, x => x.Id == product.Id);
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder
                .UseEnvironment("Testing")
                .ConfigureServices(services =>
                {
                    services.RemoveAll<ProductHubDbContext>();
                    services.RemoveAll<DbContextOptions<ProductHubDbContext>>();
                    services.AddDbContext<ProductHubDbContext>(options =>
                        options.UseSqlServer(database.ConnectionString)
                    );
                })
        );
}
