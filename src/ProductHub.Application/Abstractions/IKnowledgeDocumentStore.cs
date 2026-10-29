using ProductHub.Domain.Knowledge;

namespace ProductHub.Application.Abstractions;

public interface IKnowledgeDocumentStore
{
    Task AddAsync(KnowledgeDocument document, CancellationToken cancellationToken);
    Task AddChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken);
    Task<KnowledgeDocument?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, DocumentChunk>> GetChunksAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken
    );
    Task<bool> ExistsByHashAsync(string sha256, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
