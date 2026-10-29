using ProductHub.Application.Knowledge;
using ProductHub.Domain.Knowledge;

namespace ProductHub.UnitTests.Knowledge;

public sealed class KnowledgeDocumentTests
{
    [Fact]
    public void Complete_CreatesOrderedChunksAndTerminalState()
    {
        var document = KnowledgeDocument.Start(
            Guid.NewGuid(),
            "manual.md",
            "text/markdown",
            new string('A', 64)
        );
        document.Complete(["first", "second"]);
        Assert.Equal(DocumentIngestionStatus.Completed, document.Status);
        Assert.Equal([0, 1], document.Chunks.Select(x => x.Ordinal));
        Assert.All(document.Chunks, x => Assert.Equal(document.Id, x.DocumentId));
    }

    [Fact]
    public void Complete_RejectsEmptyText()
    {
        var document = KnowledgeDocument.Start(
            Guid.NewGuid(),
            "manual.md",
            "text/markdown",
            new string('A', 64)
        );
        Assert.Throws<ArgumentException>(() => document.Complete([" "]));
    }

    [Fact]
    public void Chunk_UsesOverlapAndPreservesEndOfDocument()
    {
        var text = string.Join(' ', Enumerable.Repeat("product-fact", 80));
        var chunks = DocumentIngestionService.Chunk(text, 240, 40);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, x => Assert.InRange(x.Length, 1, 240));
        Assert.EndsWith("product-fact", chunks[^1]);
    }
}
