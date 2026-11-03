using ProductHub.Application.Abstractions;
using ProductHub.Application.Knowledge;
using ProductHub.Application.Products;
using ProductHub.Domain.Knowledge;

namespace ProductHub.UnitTests.Knowledge;

public sealed class KnowledgeSearchServiceTests
{
    [Fact]
    public async Task Naive_UsesSingleQueryAndPreservesVectorRanking()
    {
        var store = new TestKnowledgeStore();
        var doc = store.Add(
            Guid.NewGuid(),
            "manual.md",
            "Bottle cleaning instructions",
            "Bottle capacity"
        );
        var embeddings = new TestEmbeddings();
        var vectors = new TestVectors
        {
            Hits = [new(doc.Chunks.Last().Id, .8f), new(doc.Chunks.First().Id, .6f)],
        };
        var results = await new KnowledgeSearchService(store, embeddings, vectors).SearchAsync(
            new("SKU | Bottle | Acme", 2, doc.ProductId),
            default
        );

        Assert.Single(embeddings.Texts);
        Assert.Single(vectors.Limits);
        Assert.Equal(2, vectors.Limits[0]);
        Assert.Equal(doc.Chunks.Last().Id, results[0].ChunkId);
        Assert.Equal(.8f, results[0].Score);
        Assert.Equal("manual.md#chunk-1", results[0].Citation);
    }

    [Fact]
    public async Task Advanced_ExpandsQueriesReranksAndRemovesNearDuplicates()
    {
        var store = new TestKnowledgeStore();
        var productId = Guid.NewGuid();
        var doc = store.Add(
            productId,
            "manual.md",
            "Use water and a brush to clean the bottle after use",
            "Use water and a brush to clean the bottle after use",
            "Store the bottle upright in a dry place"
        );
        var embeddings = new TestEmbeddings();
        var vectors = new TestVectors
        {
            Hits = doc.Chunks.Select(x => new VectorSearchHit(x.Id, .8f)).ToArray(),
        };

        var results = await new KnowledgeSearchService(store, embeddings, vectors).SearchAsync(
            new("SKU | Bottle | Acme | Material: Steel", 3, productId, RetrievalMode.Advanced),
            default
        );

        Assert.Equal(3, embeddings.Texts.Count);
        Assert.Equal(3, vectors.Limits.Count);
        Assert.All(vectors.Limits, limit => Assert.Equal(12, limit));
        Assert.All(vectors.ProductIds, id => Assert.Equal(productId, id));
        Assert.Equal(2, results.Count);
        Assert.Contains(
            results,
            result => result.Text.StartsWith("Store", StringComparison.Ordinal)
        );
        Assert.All(results, result => Assert.InRange(result.Score, 0f, 1f));
        Assert.Equal(results.Count, results.Select(x => x.ChunkId).Distinct().Count());
    }

    [Theory]
    [InlineData(RetrievalMode.Naive)]
    [InlineData(RetrievalMode.Advanced)]
    public async Task Search_DiscardsStaleAndOtherProductsHits(RetrievalMode mode)
    {
        var store = new TestKnowledgeStore();
        var productId = Guid.NewGuid();
        var own = store.Add(productId, "own.md", "Bottle instructions");
        var other = store.Add(Guid.NewGuid(), "other.md", "Other private product");
        var vectors = new TestVectors
        {
            Hits =
            [
                new(Guid.NewGuid(), 1),
                new(other.Chunks.Single().Id, .9f),
                new(own.Chunks.Single().Id, .8f),
            ],
        };
        var results = await new KnowledgeSearchService(
            store,
            new TestEmbeddings(),
            vectors
        ).SearchAsync(new("Bottle", 3, productId, mode), default);
        Assert.Equal(own.Id, Assert.Single(results).DocumentId);
    }

    [Fact]
    public async Task Advanced_FusesCandidateUnionAndPromotesConsistentRelevantEvidence()
    {
        var store = new TestKnowledgeStore();
        var productId = Guid.NewGuid();
        var doc = store.Add(
            productId,
            "manual.md",
            "Unrelated shipping policy",
            "Bottle material steel maintenance"
        );
        var unrelated = new VectorSearchHit(doc.Chunks.First().Id, .9f);
        var relevant = new VectorSearchHit(doc.Chunks.Last().Id, .8f);
        var vectors = new TestVectors
        {
            Rankings =
            [
                [unrelated],
                [relevant],
                [relevant],
            ],
        };
        var results = await new KnowledgeSearchService(
            store,
            new TestEmbeddings(),
            vectors
        ).SearchAsync(
            new("SKU | Bottle | Acme | Material: Steel", 2, productId, RetrievalMode.Advanced),
            default
        );
        Assert.Equal(2, results.Count);
        Assert.Equal(relevant.Id, results[0].ChunkId);
        Assert.Equal(1f, results[0].Score);
    }

    [Fact]
    public async Task Advanced_BoundsCandidatePoolAndContextSize()
    {
        var store = new TestKnowledgeStore();
        var documents = Enumerable
            .Range(0, 20)
            .Select(index =>
                store.Add(
                    Guid.Empty,
                    $"manual-{index}.md",
                    string.Join(" ", Enumerable.Range(0, 400).Select(x => $"term{index}x{x}"))
                )
            )
            .ToArray();
        var vectors = new TestVectors
        {
            Hits = documents.Select(x => new VectorSearchHit(x.Chunks.Single().Id, .8f)).ToArray(),
        };
        var results = await new KnowledgeSearchService(
            store,
            new TestEmbeddings(),
            vectors
        ).SearchAsync(new("Bottle", 20, null, RetrievalMode.Advanced), default);
        Assert.NotEmpty(results);
        Assert.All(vectors.Limits, limit => Assert.Equal(80, limit));
        Assert.True(results.Sum(x => x.Text.Length) <= 12000);
        Assert.All(results, result => Assert.True(result.Text.Length <= 2400));
    }

    [Theory]
    [InlineData(0, RetrievalMode.Advanced)]
    [InlineData(21, RetrievalMode.Naive)]
    [InlineData(5, RetrievalMode.None)]
    public async Task Search_RejectsInvalidSettingsBeforeEmbedding(int limit, RetrievalMode mode)
    {
        var embeddings = new TestEmbeddings();
        await Assert.ThrowsAsync<ValidationException>(() =>
            new KnowledgeSearchService(
                new TestKnowledgeStore(),
                embeddings,
                new TestVectors()
            ).SearchAsync(new("Bottle", limit, Mode: mode), default)
        );
        Assert.Empty(embeddings.Texts);
    }

    [Fact]
    public async Task Advanced_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new KnowledgeSearchService(
                new TestKnowledgeStore(),
                new TestEmbeddings(),
                new TestVectors()
            ).SearchAsync(new("Bottle", Mode: RetrievalMode.Advanced), cancellation.Token)
        );
    }
}

internal sealed class TestEmbeddings : ITextEmbeddingGenerator
{
    public List<string> Texts { get; } = [];

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        Texts.AddRange(texts);
        return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(
            texts.Select((_, i) => new ReadOnlyMemory<float>(new[] { (float)i })).ToArray()
        );
    }
}

internal sealed class TestVectors : IVectorStore
{
    public IReadOnlyList<VectorSearchHit> Hits { get; init; } = [];
    public List<int> Limits { get; } = [];
    public List<Guid?> ProductIds { get; } = [];
    public Exception? Error { get; init; }
    public IReadOnlyList<IReadOnlyList<VectorSearchHit>>? Rankings { get; init; }

    public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        ReadOnlyMemory<float> vector,
        int limit,
        Guid? productId,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        Limits.Add(limit);
        ProductIds.Add(productId);
        var results = Rankings is null
            ? Hits
            : Rankings[Math.Min(Limits.Count - 1, Rankings.Count - 1)];
        return Error is null
            ? Task.FromResult(results)
            : Task.FromException<IReadOnlyList<VectorSearchHit>>(Error);
    }

    public Task UpsertAsync(IReadOnlyList<VectorDocument> documents, CancellationToken ct) =>
        Task.CompletedTask;

    public Task DeleteDocumentAsync(Guid documentId, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class TestKnowledgeStore : IKnowledgeDocumentStore
{
    private readonly List<KnowledgeDocument> documents = [];

    public KnowledgeDocument Add(Guid productId, string name, params string[] chunks)
    {
        var document = KnowledgeDocument.Start(
            productId == Guid.Empty ? Guid.NewGuid() : productId,
            name,
            "text/plain",
            Guid.NewGuid().ToString()
        );
        document.Complete(chunks);
        // EF normally fixes up the navigation when hydrating chunks.
        foreach (var chunk in document.Chunks)
            typeof(DocumentChunk)
                .GetProperty(nameof(DocumentChunk.Document))!
                .SetValue(chunk, document);
        documents.Add(document);
        return document;
    }

    public Task<IReadOnlyDictionary<Guid, DocumentChunk>> GetChunksAsync(
        IEnumerable<Guid> ids,
        CancellationToken ct
    )
    {
        var wanted = ids.ToHashSet();
        return Task.FromResult<IReadOnlyDictionary<Guid, DocumentChunk>>(
            documents
                .SelectMany(x => x.Chunks)
                .Where(x => wanted.Contains(x.Id))
                .ToDictionary(x => x.Id)
        );
    }

    public Task AddAsync(KnowledgeDocument document, CancellationToken ct) => Task.CompletedTask;

    public Task AddChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<KnowledgeDocument?> GetAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(documents.Find(x => x.Id == id));

    public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(Guid productId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<KnowledgeDocument>>(
            documents.Where(x => x.ProductId == productId).ToArray()
        );

    public Task<bool> ExistsByHashAsync(Guid productId, string sha256, CancellationToken ct) =>
        Task.FromResult(false);

    public void Remove(KnowledgeDocument document) => documents.Remove(document);

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}
