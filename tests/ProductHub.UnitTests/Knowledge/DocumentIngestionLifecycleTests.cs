using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductHub.Application.Knowledge;
using ProductHub.Domain.Knowledge;

namespace ProductHub.UnitTests.Knowledge;

public sealed class DocumentIngestionLifecycleTests
{
    [Fact]
    public async Task FailedVectorWrite_NeverMarksDocumentReadyForLexicalOrGraphRetrieval()
    {
        var store = new TestKnowledgeStore();
        var id = Guid.NewGuid();
        var vectors = new TestVectors
        {
            OnUpsert = () => throw new IOException("Vector write failed"),
        };
        var service = new DocumentIngestionService(
            store,
            new Extractor(),
            new TestEmbeddings(),
            vectors,
            Options.Create(new RagSettings()),
            NullLogger<DocumentIngestionService>.Instance
        );
        await Assert.ThrowsAsync<DocumentIngestionException>(() =>
            service.IngestAsync(new(id, "failure.md", "text/plain", [1, 2, 3]), default)
        );
        var failed = Assert.Single(await store.ListAsync(id, default));
        Assert.Equal(DocumentIngestionStatus.Failed, failed.Status);
        Assert.Equal("ingestion-failed", failed.ErrorCode);
        Assert.Empty(await store.GetGraphChunksAsync(id, [], 500, default));
        Assert.Empty(await store.SearchLexicalChunksAsync(id, ["Thermostat"], 80, default));
    }

    [Fact]
    public async Task VectorWrite_SeesPendingDocumentUntilAllWritesSucceed()
    {
        var store = new TestKnowledgeStore();
        var id = Guid.NewGuid();
        var vectors = new TestVectors
        {
            OnUpsert = async () =>
                Assert.Equal(
                    DocumentIngestionStatus.Pending,
                    Assert.Single(await store.ListAsync(id, default)).Status
                ),
        };
        var service = new DocumentIngestionService(
            store,
            new Extractor(),
            new TestEmbeddings(),
            vectors,
            Options.Create(new RagSettings()),
            NullLogger<DocumentIngestionService>.Instance
        );
        var result = await service.IngestAsync(
            new(id, "success.md", "text/plain", [4, 5, 6]),
            default
        );
        Assert.Equal(DocumentIngestionStatus.Completed, result.Status);
        Assert.StartsWith("local-extractor-v1", result.ExtractionVersion);
    }

    private sealed class Extractor : IDocumentTextExtractor
    {
        public Task<ExtractedDocument> ExtractAsync(
            string name,
            string type,
            ReadOnlyMemory<byte> content,
            CancellationToken ct
        ) => Task.FromResult(new ExtractedDocument("Thermostat guidance: disconnect the mains."));
    }
}
