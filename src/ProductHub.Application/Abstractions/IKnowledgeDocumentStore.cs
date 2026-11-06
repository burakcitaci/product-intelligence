using ProductHub.Domain.Knowledge;

namespace ProductHub.Application.Abstractions;

public interface IKnowledgeDocumentStore
{
    Task AddAsync(KnowledgeDocument document, CancellationToken cancellationToken);
    Task AddChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken);
    Task<KnowledgeDocument?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeDocument>> ListAsync(
        Guid productId,
        CancellationToken cancellationToken
    );
    Task<IReadOnlyDictionary<Guid, DocumentChunk>> GetChunksAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken
    );
    Task<IReadOnlyList<DocumentChunk>> GetGraphChunksAsync(
        Guid productId,
        IReadOnlyCollection<Guid> seedChunkIds,
        int limit,
        CancellationToken cancellationToken
    );
    Task<IReadOnlyList<DocumentChunk>> SearchLexicalChunksAsync(
        Guid? productId,
        IReadOnlyList<string> terms,
        int limit,
        CancellationToken cancellationToken
    );
    Task<bool> ExistsByHashAsync(
        Guid productId,
        string sha256,
        CancellationToken cancellationToken
    );
    void Remove(KnowledgeDocument document);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
