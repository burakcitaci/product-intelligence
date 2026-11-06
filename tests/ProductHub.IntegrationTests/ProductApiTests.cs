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

    [Fact]
    public async Task ReviewQueue_ScopesCatalogAndLanguageAndUsesPublishedGenerationTime()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var catalogResponse = await client.PostAsJsonAsync(
            "/api/catalogs",
            new { name = $"Queue-{Guid.NewGuid():N}" }
        );
        var catalog = (await catalogResponse.Content.ReadFromJsonAsync<CatalogDto>())!;
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var created = await client.PostAsJsonAsync(
                "/api/products",
                new
                {
                    sku = $"Q-{i}-{Guid.NewGuid():N}",
                    name = "Bottle",
                    brand = "Queue",
                    catalogId = catalog.Id,
                    attributes = i == 0
                        ? Array.Empty<ProductAttributeData>()
                        : [new ProductAttributeData("Capacity", "750", "ml")],
                }
            );
            ids.Add((await created.Content.ReadFromJsonAsync<ProductDto>())!.Id);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProductHubDbContext>();
            for (var i = 0; i < ids.Count; i++)
            {
                var language = i == 2 ? "en" : "de";
                var generation = ProductHub.Domain.Content.AiGeneration.Start(
                    ids[i],
                    Guid.NewGuid().ToString(),
                    "Test",
                    "Test",
                    "Test",
                    language,
                    ProductHub.Domain.Content.GroundingMode.FactsOnly,
                    "queue-test",
                    now: DateTimeOffset.UtcNow.AddDays(-1)
                );
                generation.Complete(0, 0);
                var draft = ProductHub.Domain.Content.ContentDraft.Create(
                    ids[i],
                    language,
                    "Bottle",
                    "Bottle",
                    "Bottle",
                    [],
                    generation.Id
                );
                if (i == 1)
                {
                    draft.Approve();
                    draft.MarkCurrent();
                }
                db.AiGenerations.Add(generation);
                db.ContentDrafts.Add(draft);
            }
            await db.SaveChangesAsync();
        }
        var de = (
            await client.GetFromJsonAsync<ReviewQueueResult>(
                $"/api/catalogs/{catalog.Id}/review-queue?language=de"
            )
        )!;
        Assert.Equal(new ReviewQueueCounts(3, 1, 2, 1, 1), de.Counts);
        var pending = (
            await client.GetFromJsonAsync<ReviewQueueResult>(
                $"/api/catalogs/{catalog.Id}/review-queue?language=de&filter=pending&page=10&pageSize=1"
            )
        )!;
        Assert.Equal(ids[0], Assert.Single(pending.Items).Product.Id);
        Assert.Equal(1, pending.Page);
        var en = (
            await client.GetFromJsonAsync<ReviewQueueResult>(
                $"/api/catalogs/{catalog.Id}/review-queue?language=en"
            )
        )!;
        Assert.Equal(new ReviewQueueCounts(3, 1, 3, 1, 0), en.Counts);
        var stale = (
            await client.GetFromJsonAsync<ReviewQueueResult>(
                $"/api/catalogs/{catalog.Id}/review-queue?language=de&filter=stale"
            )
        )!;
        Assert.Equal(ids[1], Assert.Single(stale.Items).Product.Id);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await client.GetAsync($"/api/catalogs/{catalog.Id}/review-queue?filter=unknown")
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/catalogs/{Guid.NewGuid()}/review-queue")).StatusCode
        );
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
