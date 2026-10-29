using Microsoft.EntityFrameworkCore;
using ProductHub.Application.Abstractions;
using ProductHub.Domain.Knowledge;

namespace ProductHub.Infrastructure.Persistence;

internal sealed class KnowledgeDocumentStore(ProductHubDbContext db) : IKnowledgeDocumentStore
{
    public async Task AddAsync(KnowledgeDocument document, CancellationToken ct) =>
        await db.KnowledgeDocuments.AddAsync(document, ct);

    public Task AddChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct) =>
        db.DocumentChunks.AddRangeAsync(chunks, ct);

    public Task<KnowledgeDocument?> GetAsync(Guid id, CancellationToken ct) =>
        db.KnowledgeDocuments.Include(x => x.Chunks).SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyDictionary<Guid, DocumentChunk>> GetChunksAsync(
        IEnumerable<Guid> ids,
        CancellationToken ct
    )
    {
        var values = ids.Distinct().ToArray();
        return await db
            .DocumentChunks.Include(x => x.Document)
            .Where(x => values.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
    }

    public Task<bool> ExistsByHashAsync(string hash, CancellationToken ct) =>
        db.KnowledgeDocuments.AnyAsync(x => x.Sha256 == hash, ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
