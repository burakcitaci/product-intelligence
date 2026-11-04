using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductHub.Application.Abstractions;
using ProductHub.Application.Content;
using ProductHub.Application.Knowledge;
using ProductHub.Application.Products;
using ProductHub.Domain.Content;
using ProductHub.Domain.Products;
using ProductHub.UnitTests.Knowledge;

namespace ProductHub.UnitTests.Content;

public sealed class ContentGenerationServiceTests
{
    private static readonly AiSettings Settings = new();

    [Fact]
    public async Task GenerateAsync_PersistsPendingDraftAndSuccessfulMetadata()
    {
        var store = new FakeStore { Product = Product.Create("SKU-1", "Bottle", "Acme") };
        var generator = new FakeGenerator();
        var result = await CreateService(store, generator)
            .GenerateAsync(
                new GenerateDraftCommand(store.Product.Id, "de", "key-1", "trace-1"),
                CancellationToken.None
            );

        Assert.Equal(ContentDraftStatus.PendingReview, result.Status);
        Assert.Equal(2, store.SaveCount);
        Assert.Equal(AiGenerationStatus.Succeeded, store.Generation!.Status);
        Assert.Equal(12, store.Generation.InputTokens);
        Assert.NotNull(store.Draft);
    }

    [Fact]
    public async Task GenerateAsync_UsesSelectedModelAndPersistsItForAudit()
    {
        var store = new FakeStore { Product = Product.Create("SKU-1", "Bottle", "Acme") };
        var generator = new FakeGenerator();

        await CreateService(store, generator)
            .GenerateAsync(
                new GenerateDraftCommand(
                    store.Product.Id,
                    "de",
                    "key-model",
                    "trace-model",
                    Model: "llama3.1:8b"
                ),
                CancellationToken.None
            );

        Assert.Equal("llama3.1:8b", store.Generation!.Model);
        Assert.Equal("llama3.1:8b", generator.LastOptions!.Model);
    }

    [Fact]
    public async Task GenerateAsync_ReplaysExistingSuccessfulDraft()
    {
        var product = Product.Create("SKU-1", "Bottle", "Acme");
        var generation = AiGeneration.Start(
            product.Id,
            "same-key",
            "Ollama",
            "model",
            "prompt",
            "de",
            GroundingMode.FactsOnly,
            "trace"
        );
        generation.Complete(1, 2);
        var draft = ContentDraft.Create(
            product.Id,
            "de",
            "Title",
            "Short",
            "Long",
            ["One", "Two", "Three"],
            generation.Id
        );
        var store = new FakeStore
        {
            Product = product,
            Generation = generation,
            Draft = draft,
        };
        var generator = new FakeGenerator();

        var result = await CreateService(store, generator)
            .GenerateAsync(
                new GenerateDraftCommand(product.Id, "de", "same-key", "trace-2"),
                CancellationToken.None
            );

        Assert.Equal(draft.Id, result.Id);
        Assert.Equal(0, generator.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_RecordsGeneratorFailure()
    {
        var store = new FakeStore { Product = Product.Create("SKU-1", "Bottle", "Acme") };
        var generator = new FakeGenerator
        {
            Exception = new ContentGenerationException("ollama-unavailable", "Offline"),
        };

        var error = await Assert.ThrowsAsync<GenerationUnavailableException>(() =>
            CreateService(store, generator)
                .GenerateAsync(
                    new GenerateDraftCommand(store.Product.Id, "en", "key-2", "trace"),
                    CancellationToken.None
                )
        );

        Assert.Equal("ollama-unavailable", error.ErrorCode);
        Assert.Equal(AiGenerationStatus.Failed, store.Generation!.Status);
        Assert.Equal("ollama-unavailable", store.Generation.ErrorCode);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task GenerateAsync_RecordsInvalidModelOutput()
    {
        var store = new FakeStore { Product = Product.Create("SKU-1", "Bottle", "Acme") };
        var generator = new FakeGenerator
        {
            Result = new GeneratedProductContent("", "Short", "Long", ["Only", "Two"]),
        };
        var error = await Assert.ThrowsAsync<GenerationUnavailableException>(() =>
            CreateService(store, generator)
                .GenerateAsync(
                    new GenerateDraftCommand(store.Product.Id, "de", "invalid", "trace"),
                    CancellationToken.None
                )
        );
        Assert.Equal("invalid-output", error.ErrorCode);
        Assert.Equal(AiGenerationStatus.Failed, store.Generation!.Status);
    }

    [Fact]
    public async Task GenerateAsync_RecordsCallerCancellation()
    {
        var store = new FakeStore { Product = Product.Create("SKU-1", "Bottle", "Acme") };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var generator = new FakeGenerator { HonorCancellation = true };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(store, generator)
                .GenerateAsync(
                    new GenerateDraftCommand(store.Product.Id, "de", "cancel", "trace"),
                    cancellation.Token
                )
        );
        Assert.Equal("cancelled", store.Generation!.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_RejectsUnsupportedLanguageBeforePersistence()
    {
        var store = new FakeStore { Product = Product.Create("SKU-1", "Bottle", "Acme") };
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService(store, new FakeGenerator())
                .GenerateAsync(
                    new GenerateDraftCommand(store.Product.Id, "fr", "key", "trace"),
                    CancellationToken.None
                )
        );
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task GenerateAsync_RejectsUnsupportedModelBeforePersistence()
    {
        var store = new FakeStore { Product = Product.Create("SKU-1", "Bottle", "Acme") };

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService(store, new FakeGenerator())
                .GenerateAsync(
                    new GenerateDraftCommand(
                        store.Product.Id,
                        "de",
                        "key-model-invalid",
                        "trace",
                        Model: "unknown:latest"
                    ),
                    CancellationToken.None
                )
        );

        Assert.Equal(0, store.SaveCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task GenerateAsync_RejectsInvalidTopKBeforePersistence(int topK)
    {
        var store = new FakeStore { Product = Product.Create("SKU-1", "Bottle", "Acme") };
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService(store, new FakeGenerator())
                .GenerateAsync(
                    new GenerateDraftCommand(
                        store.Product.Id,
                        "de",
                        "key",
                        "trace",
                        UseRag: true,
                        TopK: topK
                    ),
                    CancellationToken.None
                )
        );
        Assert.Equal(0, store.SaveCount);
    }

    [Theory]
    [InlineData(true, "None", GroundingMode.FactsOnly)]
    [InlineData(false, "None", GroundingMode.IdentityOnly)]
    [InlineData(true, "Naive", GroundingMode.Rag)]
    [InlineData(false, "Naive", GroundingMode.Rag)]
    [InlineData(true, "Advanced", GroundingMode.AdvancedRag)]
    [InlineData(false, "Advanced", GroundingMode.AdvancedRag)]
    [InlineData(true, "Graph", GroundingMode.GraphRag)]
    [InlineData(false, "Graph", GroundingMode.GraphRag)]
    public async Task GenerateAsync_SupportsAllFactsAndRetrievalCombinations(
        bool includeFacts,
        string mode,
        GroundingMode expectedMode
    )
    {
        var product = Product.Create(
            "SKU-1",
            "Bottle",
            "Acme",
            attributes: [new ProductAttribute("SecretSpecification", "SensitiveValue")]
        );
        var store = new FakeStore { Product = product };
        var knowledge = new TestKnowledgeStore();
        var doc = knowledge.Add(product.Id, "care.md", "Clean the bottle with water.");
        var embeddings = new TestEmbeddings();
        var vectors = new TestVectors { Hits = [new(doc.Chunks.Single().Id, .9f)] };
        var search = new KnowledgeSearchService(knowledge, embeddings, vectors);
        var generator = new FakeGenerator();
        var service = CreateService(store, generator, search);
        await service.GenerateAsync(
            new(
                product.Id,
                "en",
                "combination",
                "trace",
                IncludeFacts: includeFacts,
                RetrievalMode: mode
            ),
            default
        );

        var context = generator.LastContext!;
        Assert.Equal(includeFacts, context.IncludeFacts);
        Assert.Equal(includeFacts ? 1 : 0, context.Attributes.Count);
        Assert.Equal("SKU-1", context.Sku);
        Assert.Equal(mode == "None" ? 0 : 1, context.Grounding.Count);
        if (!includeFacts)
            Assert.All(
                embeddings.Texts,
                text =>
                {
                    Assert.DoesNotContain("SecretSpecification", text);
                    Assert.DoesNotContain("SensitiveValue", text);
                }
            );
        Assert.Equal(expectedMode, store.Generation!.GroundingMode);
        Assert.Equal(includeFacts, store.Generation.IncludeFacts);
        Assert.Equal(mode == "None" ? null : (int?)5, store.Generation.RequestedTopK);
        var metadata = await service.GetGenerationMetadataAsync(store.Draft!.Id, default);
        Assert.Equal(mode, metadata.RetrievalMode);
        Assert.Equal(includeFacts, metadata.IncludeFacts);
    }

    [Theory]
    [InlineData(null, true, GroundingMode.Rag)]
    [InlineData(null, false, GroundingMode.FactsOnly)]
    [InlineData("None", true, GroundingMode.FactsOnly)]
    [InlineData("Advanced", false, GroundingMode.AdvancedRag)]
    public async Task GenerateAsync_ExplicitModeTakesPrecedenceOverLegacyFlag(
        string? mode,
        bool legacy,
        GroundingMode expected
    )
    {
        var product = Product.Create("SKU-1", "Bottle", "Acme");
        var store = new FakeStore { Product = product };
        var knowledge = new TestKnowledgeStore();
        var doc = knowledge.Add(product.Id, "manual.md", "Bottle storage instructions");
        var search = new KnowledgeSearchService(
            knowledge,
            new TestEmbeddings(),
            new TestVectors { Hits = [new(doc.Chunks.Single().Id, .9f)] }
        );
        await CreateService(store, new FakeGenerator(), search)
            .GenerateAsync(
                new(product.Id, "en", "legacy", "trace", UseRag: legacy, RetrievalMode: mode),
                default
            );
        Assert.Equal(expected, store.Generation!.GroundingMode);
    }

    [Theory]
    [InlineData("Advanced", false)]
    [InlineData("Naive", true)]
    [InlineData("Graph", false)]
    [InlineData("Graph", true)]
    public async Task GenerateAsync_RetrievalFailureDoesNotSilentlyFallBack(
        string mode,
        bool unavailable
    )
    {
        var product = Product.Create("SKU-1", "Bottle", "Acme");
        var store = new FakeStore { Product = product };
        var generator = new FakeGenerator();
        var search = new KnowledgeSearchService(
            new TestKnowledgeStore(),
            new TestEmbeddings(),
            new TestVectors { Error = unavailable ? new HttpRequestException("Offline") : null }
        );
        var error = await Assert.ThrowsAsync<GenerationUnavailableException>(() =>
            CreateService(store, generator, search)
                .GenerateAsync(
                    new(
                        product.Id,
                        "en",
                        "failure",
                        "trace",
                        IncludeFacts: false,
                        RetrievalMode: mode
                    ),
                    default
                )
        );
        Assert.Equal(unavailable ? "rag-retrieval-failed" : "rag-no-context", error.ErrorCode);
        Assert.Equal(RetrievalStatus.Failed, store.Generation!.RetrievalStatus);
        Assert.Equal(AiGenerationStatus.Failed, store.Generation.Status);
        Assert.Null(store.Draft);
        Assert.Equal(0, generator.CallCount);
    }

    [Theory]
    [InlineData("Unknown", 5)]
    [InlineData("Advanced", 0)]
    [InlineData("Naive", 21)]
    [InlineData("Graph", 0)]
    [InlineData("Graph", 21)]
    public async Task GenerateAsync_RejectsInvalidRetrievalOptions(string mode, int topK)
    {
        var store = new FakeStore { Product = Product.Create("SKU-1", "Bottle", "Acme") };
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService(store, new FakeGenerator())
                .GenerateAsync(
                    new(
                        store.Product.Id,
                        "en",
                        "invalid",
                        "trace",
                        TopK: topK,
                        RetrievalMode: mode
                    ),
                    default
                )
        );
        Assert.Equal(0, store.SaveCount);
    }

    private static ContentGenerationService CreateService(
        FakeStore store,
        FakeGenerator generator,
        KnowledgeSearchService? search = null
    ) =>
        new(
            store,
            generator,
            Options.Create(Settings),
            NullLogger<ContentGenerationService>.Instance,
            search
        );

    private sealed class FakeGenerator : IContentGenerator
    {
        public int CallCount { get; private set; }
        public Exception? Exception { get; init; }
        public GeneratedProductContent? Result { get; init; }
        public bool HonorCancellation { get; init; }
        public GenerationOptions? LastOptions { get; private set; }
        public ProductContentContext? LastContext { get; private set; }

        public Task<GeneratedProductContent> GenerateAsync(
            ProductContentContext context,
            GenerationOptions options,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            LastOptions = options;
            LastContext = context;
            if (HonorCancellation)
                return Task.FromCanceled<GeneratedProductContent>(cancellationToken);
            if (Exception is not null)
                return Task.FromException<GeneratedProductContent>(Exception);
            return Task.FromResult(
                Result
                    ?? new GeneratedProductContent(
                        "Title",
                        "Short",
                        "Long description",
                        ["One", "Two", "Three"],
                        12,
                        24
                    )
            );
        }
    }

    private sealed class FakeStore : IContentGenerationStore
    {
        public Product Product { get; init; } = null!;
        public AiGeneration? Generation { get; set; }
        public ContentDraft? Draft { get; set; }
        public int SaveCount { get; private set; }

        public Task<Product?> GetProductAsync(
            Guid productId,
            CancellationToken cancellationToken
        ) => Task.FromResult<Product?>(Product.Id == productId ? Product : null);

        public Task<AiGeneration?> GetGenerationAsync(
            Guid productId,
            string idempotencyKey,
            CancellationToken cancellationToken
        ) => Task.FromResult(Generation?.IdempotencyKey == idempotencyKey ? Generation : null);

        public Task<ContentDraft?> GetDraftByGenerationAsync(
            Guid generationId,
            CancellationToken cancellationToken
        ) => Task.FromResult(Draft?.GenerationId == generationId ? Draft : null);

        public Task<ContentDraft?> GetDraftAsync(
            Guid draftId,
            CancellationToken cancellationToken
        ) => Task.FromResult(Draft?.Id == draftId ? Draft : null);

        public Task<ContentDraft?> GetDraftForUpdateAsync(
            Guid draftId,
            CancellationToken cancellationToken
        ) => Task.FromResult(Draft?.Id == draftId ? Draft : null);

        public Task<IReadOnlyList<ContentDraft>> ListDraftsForUpdateAsync(
            Guid productId,
            string language,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<IReadOnlyList<ContentDraft>>(
                Draft is not null && Draft.ProductId == productId && Draft.Language == language
                    ? [Draft]
                    : []
            );

        public Task<AiGeneration?> GetGenerationByIdAsync(
            Guid generationId,
            CancellationToken cancellationToken
        ) => Task.FromResult(Generation?.Id == generationId ? Generation : null);

        public Task<IReadOnlyList<ContentDraft>> ListDraftsAsync(
            Guid productId,
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlyList<ContentDraft>>(Draft is null ? [] : [Draft]);

        public Task AddGenerationAsync(AiGeneration generation, CancellationToken cancellationToken)
        {
            Generation = generation;
            return Task.CompletedTask;
        }

        public Task AddDraftAsync(ContentDraft draft, CancellationToken cancellationToken)
        {
            Draft = draft;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
