using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductHub.Application.Content;
using ProductHub.Application.Products;
using ProductHub.Domain.Content;
using ProductHub.Infrastructure.Persistence;

namespace ProductHub.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class ContentDraftApiTests(SqlServerFixture database)
{
    [Fact]
    public async Task GenerateThenRead_PersistsDraftWithoutOllama()
    {
        var fake = new FakeContentGenerator();
        await using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();
        var productResponse = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = $"AI-{Guid.NewGuid():N}",
                name = "Trail Bottle",
                brand = "Northwind",
                attributes = new[]
                {
                    new
                    {
                        name = "Volume",
                        value = "750",
                        unit = "ml",
                    },
                },
            }
        );
        var product = await productResponse.Content.ReadFromJsonAsync<ProductDto>();

        var idempotencyKey = $"test-{Guid.NewGuid():N}";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/products/{product!.Id}/drafts/generate"
        )
        {
            Content = JsonContent.Create(new { language = "de" }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = await response.Content.ReadFromJsonAsync<ContentDraftDto>();
        Assert.NotNull(draft);
        Assert.Equal("de", draft.Language);
        Assert.Equal("Faktischer Titel", draft.Title);
        Assert.Equal(1, fake.CallCount);

        using var replayRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/products/{product.Id}/drafts/generate"
        )
        {
            Content = JsonContent.Create(new { language = "de" }),
        };
        replayRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        var replay = await (
            await client.SendAsync(replayRequest)
        ).Content.ReadFromJsonAsync<ContentDraftDto>();
        Assert.Equal(draft.Id, replay!.Id);
        Assert.Equal(1, fake.CallCount);

        var stored = await client.GetFromJsonAsync<ContentDraftDto>($"/api/drafts/{draft.Id}");
        Assert.Equal(draft.Id, stored!.Id);
        var list = await client.GetFromJsonAsync<List<ContentDraftDto>>(
            $"/api/products/{product.Id}/drafts"
        );
        Assert.Contains(list!, x => x.Id == draft.Id);
    }

    [Fact]
    public async Task Generate_WithoutIdempotencyKey_ReturnsBadRequest()
    {
        await using var factory = CreateFactory(new FakeContentGenerator());
        using var client = factory.CreateClient();
        var productResponse = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = $"NO-KEY-{Guid.NewGuid():N}",
                name = "Item",
                brand = "Brand",
                attributes = Array.Empty<object>(),
            }
        );
        var product = await productResponse.Content.ReadFromJsonAsync<ProductDto>();
        var response = await client.PostAsJsonAsync(
            $"/api/products/{product!.Id}/drafts/generate",
            new { language = "de" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Review_ApprovesDraftAndExposesGenerationMetadata()
    {
        await using var factory = CreateFactory(new FakeContentGenerator());
        using var client = factory.CreateClient();
        var productResponse = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = $"REVIEW-{Guid.NewGuid():N}",
                name = "Review Item",
                brand = "Northwind",
                attributes = Array.Empty<object>(),
            }
        );
        var product = await productResponse.Content.ReadFromJsonAsync<ProductDto>();
        using var generate = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/products/{product!.Id}/drafts/generate"
        )
        {
            Content = JsonContent.Create(new { language = "en" }),
        };
        generate.Headers.Add("Idempotency-Key", $"review-{Guid.NewGuid():N}");
        var draft = await (
            await client.SendAsync(generate)
        ).Content.ReadFromJsonAsync<ContentDraftDto>();

        var editResponse = await client.PutAsJsonAsync(
            $"/api/drafts/{draft!.Id}",
            new
            {
                title = "Human-edited title",
                shortDescription = "Human-edited short copy",
                longDescription = "Human-edited long copy for the product.",
                highlights = new[] { "Edited fact one", "Edited fact two", "Edited fact three" },
            }
        );
        var edited = await editResponse.Content.ReadFromJsonAsync<ContentDraftDto>();
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        Assert.Equal("Human-edited title", edited!.Title);

        var approvedResponse = await client.PostAsync($"/api/drafts/{draft.Id}/approve", null);
        var approved = await approvedResponse.Content.ReadFromJsonAsync<ContentDraftDto>();
        Assert.Equal(HttpStatusCode.OK, approvedResponse.StatusCode);
        Assert.Equal(ContentDraftStatus.Approved, approved!.Status);

        var editAfterApproval = await client.PutAsJsonAsync(
            $"/api/drafts/{draft.Id}",
            new
            {
                title = "Too late",
                shortDescription = "Short",
                longDescription = "Long",
                highlights = new[] { "One", "Two", "Three" },
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, editAfterApproval.StatusCode);

        var rejectedAfterApproval = await client.PostAsync($"/api/drafts/{draft.Id}/reject", null);
        Assert.Equal(HttpStatusCode.Conflict, rejectedAfterApproval.StatusCode);

        var metadata = await client.GetFromJsonAsync<AiGenerationMetadataDto>(
            $"/api/drafts/{draft.Id}/generation"
        );
        Assert.Equal("Ollama", metadata!.Provider);
        Assert.Equal(draft.GenerationId, metadata.Id);
    }

    private WebApplicationFactory<Program> CreateFactory(IContentGenerator generator) =>
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
                    services.RemoveAll<IContentGenerator>();
                    services.AddSingleton(generator);
                })
        );

    private sealed class FakeContentGenerator : IContentGenerator
    {
        public int CallCount { get; private set; }

        public Task<GeneratedProductContent> GenerateAsync(
            ProductContentContext context,
            GenerationOptions options,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            return Task.FromResult(
                new GeneratedProductContent(
                    "Faktischer Titel",
                    "Kurze Beschreibung",
                    "Eine längere, ausschließlich faktische Beschreibung.",
                    ["Fakt eins", "Fakt zwei", "Fakt drei"],
                    10,
                    20
                )
            );
        }
    }
}
