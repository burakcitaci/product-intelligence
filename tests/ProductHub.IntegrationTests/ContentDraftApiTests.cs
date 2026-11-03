using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductHub.Application.Content;
using ProductHub.Application.Knowledge;
using ProductHub.Application.Products;
using ProductHub.Domain.Content;
using ProductHub.Domain.Knowledge;
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
        Assert.Equal(HttpStatusCode.OK, rejectedAfterApproval.StatusCode);
        var rejected = await rejectedAfterApproval.Content.ReadFromJsonAsync<ContentDraftDto>();
        Assert.Equal(ContentDraftStatus.Rejected, rejected!.Status);
        Assert.False(rejected.IsCurrent);

        var metadata = await client.GetFromJsonAsync<AiGenerationMetadataDto>(
            $"/api/drafts/{draft.Id}/generation"
        );
        Assert.Equal("Ollama", metadata!.Provider);
        Assert.Equal(draft.GenerationId, metadata.Id);
    }

    [Theory]
    [InlineData(true, "None")]
    [InlineData(false, "None")]
    [InlineData(true, "Naive")]
    [InlineData(false, "Naive")]
    [InlineData(true, "Advanced")]
    [InlineData(false, "Advanced")]
    public async Task Generate_SelectedGroundingOptionsRoundTripThroughDatabase(
        bool includeFacts,
        string mode
    )
    {
        var generator = new FakeContentGenerator();
        var vectors = new FakeVectors();
        await using var factory = CreateFactory(generator, vectors);
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/products",
            new
            {
                sku = $"RAG-{Guid.NewGuid():N}",
                name = "Bottle",
                brand = "Acme",
                attributes = new[]
                {
                    new
                    {
                        name = "Capacity",
                        value = "750",
                        unit = "ml",
                    },
                },
            }
        );
        var product = (await response.Content.ReadFromJsonAsync<ProductDto>())!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProductHubDbContext>();
            Assert.False(db.Database.HasPendingModelChanges());
            var document = KnowledgeDocument.Start(
                product.Id,
                "manual.md",
                "text/plain",
                Guid.NewGuid().ToString("N")
            );
            document.Complete(["Clean the bottle with warm water."]);
            db.KnowledgeDocuments.Add(document);
            await db.SaveChangesAsync();
            vectors.Hits = [new(document.Chunks.Single().Id, .9f)];
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/products/{product.Id}/drafts/generate"
        )
        {
            Content = JsonContent.Create(
                new
                {
                    language = "en",
                    includeFacts,
                    retrievalMode = mode,
                    topK = 3,
                }
            ),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var generated = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, generated.StatusCode);
        Assert.NotNull(generated.Headers.Location);
        var draft = (await generated.Content.ReadFromJsonAsync<ContentDraftDto>())!;
        var metadata = (
            await client.GetFromJsonAsync<AiGenerationMetadataDto>(
                $"/api/drafts/{draft.Id}/generation"
            )
        )!;
        Assert.Equal(includeFacts, metadata.IncludeFacts);
        Assert.Equal(mode, metadata.RetrievalMode);
        Assert.Equal(mode == "None" ? "NotRequested" : "Succeeded", metadata.RetrievalStatus);
        Assert.Equal(mode == "None" ? null : (int?)3, metadata.RequestedTopK);
        Assert.Equal(includeFacts ? 1 : 0, generator.LastContext!.Attributes.Count);
        Assert.Equal(mode == "None" ? 0 : 1, generator.LastContext.Grounding.Count);
    }

    [Fact]
    public async Task Generate_InvalidRetrievalModeReturnsValidationProblem()
    {
        await using var factory = CreateFactory(new FakeContentGenerator());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/products/{Guid.NewGuid()}/drafts/generate"
        )
        {
            Content = JsonContent.Create(new { retrievalMode = "Unknown" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("retrievalMode", await response.Content.ReadAsStringAsync());
    }

    private WebApplicationFactory<Program> CreateFactory(
        IContentGenerator generator,
        FakeVectors? vectors = null
    ) =>
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
                    services.RemoveAll<ITextEmbeddingGenerator>();
                    services.AddSingleton<ITextEmbeddingGenerator, FakeEmbeddings>();
                    services.RemoveAll<IVectorStore>();
                    services.AddSingleton<IVectorStore>(vectors ?? new FakeVectors());
                })
        );

    private sealed class FakeEmbeddings : ITextEmbeddingGenerator
    {
        public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct
        ) =>
            Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(
                texts.Select(_ => new ReadOnlyMemory<float>(new float[] { 1 })).ToArray()
            );
    }

    private sealed class FakeVectors : IVectorStore
    {
        public IReadOnlyList<VectorSearchHit> Hits { get; set; } = [];

        public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
            ReadOnlyMemory<float> vector,
            int limit,
            Guid? productId,
            CancellationToken ct
        ) => Task.FromResult(Hits);

        public Task UpsertAsync(IReadOnlyList<VectorDocument> documents, CancellationToken ct) =>
            Task.CompletedTask;

        public Task DeleteDocumentAsync(Guid documentId, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class FakeContentGenerator : IContentGenerator
    {
        public int CallCount { get; private set; }
        public ProductContentContext? LastContext { get; private set; }

        public Task<GeneratedProductContent> GenerateAsync(
            ProductContentContext context,
            GenerationOptions options,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            LastContext = context;
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
