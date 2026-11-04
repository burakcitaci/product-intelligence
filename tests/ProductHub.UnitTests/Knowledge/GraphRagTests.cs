using ProductHub.Application.Knowledge;
using ProductHub.Application.Products;

namespace ProductHub.UnitTests.Knowledge;

public sealed class GraphRagTests
{
    [Fact]
    public async Task Graph_FindsTwoHopEvidenceOutsideVectorResultsAndRecordsPaths()
    {
        var productId = Guid.NewGuid();
        var store = new TestKnowledgeStore();
        var root = store.Add(productId, "control.md", "Thermostat controls kettle temperature.");
        var bridge = store.Add(productId, "care.md", "Thermostat needs regular descaling.");
        var leaf = store.Add(productId, "cleaning.md", "Descaling removes mineral residue.");
        var vectors = new TestVectors { Hits = [new(root.Chunks.Single().Id, .9f)] };

        var results = await Search(store, vectors, productId, 3);
        Assert.Equal(3, results.Count);
        Assert.Equal(0, results[0].GraphHops);
        var distant = Assert.Single(results, x => x.DocumentId == leaf.Id);
        Assert.Equal(2, distant.GraphHops);
        Assert.Equal(
            new[]
            {
                "control.md#chunk-0",
                "shared-concept:thermostat",
                "care.md#chunk-0",
                "shared-concept:descaling",
                "cleaning.md#chunk-0",
            },
            distant.GraphPath
        );
        Assert.Contains(results, x => x.DocumentId == bridge.Id && x.GraphHops == 1);
        Assert.All(results, result => Assert.InRange(result.Score, 0f, 1f));
    }

    [Fact]
    public async Task Graph_UsesDocumentAdjacencyWithoutInventingConceptRelations()
    {
        var productId = Guid.NewGuid();
        var store = new TestKnowledgeStore();
        var doc = store.Add(
            productId,
            "manual.md",
            "Electrical input socket.",
            "Disconnect mains before servicing.",
            "Allow components to cool fully.",
            "Unreachable appendix."
        );
        var vectors = new TestVectors { Hits = [new(doc.Chunks.First().Id, .9f)] };

        var results = await Search(store, vectors, productId, 5);
        Assert.Equal(3, results.Count);
        Assert.DoesNotContain(results, result => result.ChunkOrdinal == 3);
        Assert.Equal(2, results.Last().GraphHops);
        Assert.Equal(
            new[]
            {
                "manual.md#chunk-0",
                "adjacent-chunk",
                "manual.md#chunk-1",
                "adjacent-chunk",
                "manual.md#chunk-2",
            },
            results.Last().GraphPath
        );
    }

    [Fact]
    public async Task Graph_DoesNotCrossProductsEvenWhenStoreReturnsUnscopedCorpus()
    {
        var productId = Guid.NewGuid();
        var store = new TestKnowledgeStore { ReturnUnscopedGraph = true };
        var root = store.Add(productId, "control.md", "Thermostat controls kettle temperature.");
        store.Add(Guid.NewGuid(), "private.md", "Thermostat secret private detail.");
        var results = await Search(
            store,
            new TestVectors { Hits = [new(root.Chunks.Single().Id, 1f)] },
            productId,
            5
        );
        Assert.Equal(root.Id, Assert.Single(results).DocumentId);
    }

    [Fact]
    public async Task Graph_DiscardsIncompleteDocumentsEvenWhenStoreReturnsThem()
    {
        var productId = Guid.NewGuid();
        var store = new TestKnowledgeStore { ReturnUnscopedGraph = true };
        var root = store.Add(productId, "control.md", "Thermostat controls kettle temperature.");
        var incomplete = store.Add(productId, "unfinished.md", "Thermostat draft private data.");
        typeof(ProductHub.Domain.Knowledge.KnowledgeDocument)
            .GetProperty(nameof(ProductHub.Domain.Knowledge.KnowledgeDocument.Status))!
            .SetValue(incomplete, ProductHub.Domain.Knowledge.DocumentIngestionStatus.Pending);
        var results = await Search(
            store,
            new TestVectors { Hits = [new(root.Chunks.Single().Id, 1f)] },
            productId,
            5
        );
        Assert.Equal(root.Id, Assert.Single(results).DocumentId);
    }

    [Fact]
    public async Task Graph_RechecksDeletedSeedsInsteadOfUsingStaleVectorContext()
    {
        var productId = Guid.NewGuid();
        var store = new TestKnowledgeStore();
        var root = store.Add(productId, "control.md", "Thermostat controls kettle temperature.");
        store.BeforeGraphRead = () => store.Remove(root);
        var results = await Search(
            store,
            new TestVectors { Hits = [new(root.Chunks.Single().Id, 1f)] },
            productId,
            5
        );
        Assert.Empty(results);
    }

    [Fact]
    public async Task Graph_HandlesCyclesWithoutRepeatingChunks()
    {
        var productId = Guid.NewGuid();
        var store = new TestKnowledgeStore();
        var root = store.Add(
            productId,
            "control.md",
            "Thermostat temperature control calibration."
        );
        store.Add(productId, "care.md", "Thermostat maintenance cleaning checks.");
        var results = await Search(
            store,
            new TestVectors { Hits = [new(root.Chunks.Single().Id, 1f)] },
            productId,
            20
        );
        Assert.Equal(2, results.Count);
        Assert.Equal(2, results.Select(x => x.ChunkId).Distinct().Count());
        Assert.All(results, result => Assert.InRange(result.GraphHops!.Value, 0, 1));
    }

    [Fact]
    public async Task Graph_RespectsTopKOneAndReturnsSeedProvenance()
    {
        var productId = Guid.NewGuid();
        var store = new TestKnowledgeStore();
        var root = store.Add(
            productId,
            "manual.md",
            "Control settings.",
            "Maintenance procedures."
        );
        var results = await Search(
            store,
            new TestVectors { Hits = [new(root.Chunks.First().Id, 1f)] },
            productId,
            1
        );
        var seed = Assert.Single(results);
        Assert.Equal(0, seed.GraphHops);
        Assert.Equal(new[] { seed.Citation }, seed.GraphPath);
    }

    [Fact]
    public async Task Graph_BoundsCorpusAndPromptContext()
    {
        var productId = Guid.NewGuid();
        var store = new TestKnowledgeStore();
        var docs = Enumerable
            .Range(0, 510)
            .Select(index =>
                store.Add(
                    productId,
                    $"manual-{index}.md",
                    string.Join(
                        " ",
                        Enumerable.Range(0, 300).Select(word => $"concept{index}x{word}")
                    )
                )
            )
            .ToArray();
        var results = await Search(
            store,
            new TestVectors
            {
                Hits = docs.Take(20)
                    .Select(doc => new VectorSearchHit(doc.Chunks.Single().Id, .9f))
                    .ToArray(),
            },
            productId,
            20
        );
        Assert.Equal(500, store.LastGraphLimit);
        Assert.NotEmpty(results);
        Assert.True(results.Count <= 20);
        Assert.True(results.Sum(x => x.Text.Length) <= 12000);
        Assert.All(results, result => Assert.True(result.Text.Length <= 2400));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Graph_RequiresProductScopeBeforeEmbedding(string? productId)
    {
        var embeddings = new TestEmbeddings();
        await Assert.ThrowsAsync<ValidationException>(() =>
            new KnowledgeSearchService(
                new TestKnowledgeStore(),
                embeddings,
                new TestVectors()
            ).SearchAsync(
                new(
                    "Kettle",
                    ProductId: productId is null ? null : Guid.Parse(productId),
                    Mode: RetrievalMode.Graph
                ),
                default
            )
        );
        Assert.Empty(embeddings.Texts);
    }

    [Fact]
    public async Task Graph_ObservesCancellationDuringGraphLoading()
    {
        var productId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var store = new TestKnowledgeStore();
        var root = store.Add(productId, "manual.md", "Thermostat settings.");
        store.BeforeGraphRead = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new KnowledgeSearchService(
                store,
                new TestEmbeddings(),
                new TestVectors { Hits = [new(root.Chunks.Single().Id, 1f)] }
            ).SearchAsync(new("Kettle", 5, productId, RetrievalMode.Graph), cancellation.Token)
        );
    }

    private static Task<IReadOnlyList<KnowledgeSearchResult>> Search(
        TestKnowledgeStore store,
        TestVectors vectors,
        Guid productId,
        int limit
    ) =>
        new KnowledgeSearchService(store, new TestEmbeddings(), vectors).SearchAsync(
            new("SKU | Kettle | Acme", limit, productId, RetrievalMode.Graph),
            default
        );
}
