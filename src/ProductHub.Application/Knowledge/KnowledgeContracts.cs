using ProductHub.Domain.Knowledge;

namespace ProductHub.Application.Knowledge;

public sealed record IngestDocumentCommand(
    Guid ProductId,
    string FileName,
    string ContentType,
    byte[] Content
);

public sealed record DocumentDto(
    Guid Id,
    Guid? ProductId,
    string FileName,
    string ContentType,
    DocumentIngestionStatus Status,
    int ChunkCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorCode
);

public sealed record SearchKnowledgeQuery(string Query, int Limit = 5, Guid? ProductId = null);

public sealed record KnowledgeSearchResult(
    Guid DocumentId,
    string FileName,
    Guid ChunkId,
    int ChunkOrdinal,
    string Text,
    float Score,
    string Citation
);

public sealed record ExtractedDocument(string Text);

public sealed record VectorDocument(
    Guid Id,
    Guid DocumentId,
    Guid? ProductId,
    int Ordinal,
    string Text,
    ReadOnlyMemory<float> Vector
);

public sealed record VectorSearchHit(Guid Id, float Score);

public interface IDocumentTextExtractor
{
    Task<ExtractedDocument> ExtractAsync(
        string fileName,
        string contentType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken
    );
}

public interface ITextEmbeddingGenerator
{
    Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken
    );
}

public interface IVectorStore
{
    Task UpsertAsync(IReadOnlyList<VectorDocument> documents, CancellationToken cancellationToken);
    Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        ReadOnlyMemory<float> vector,
        int limit,
        Guid? productId,
        CancellationToken cancellationToken
    );
}
