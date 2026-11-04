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

    public async Task<IReadOnlyList<KnowledgeDocument>> ListAsync(
        Guid productId,
        CancellationToken ct
    ) =>
        await db
            .KnowledgeDocuments.Include(x => x.Chunks)
            .Where(x => x.ProductId == productId)
            .OrderByDescending(x => x.CreatedAt)
            .ToArrayAsync(ct);

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

    public async Task<IReadOnlyList<DocumentChunk>> GetGraphChunksAsync(
        Guid productId,
        IReadOnlyCollection<Guid> seedChunkIds,
        int limit,
        CancellationToken ct
    )
    {
        var seeds = seedChunkIds.ToArray();
        return await db
            .DocumentChunks.AsNoTracking()
            .Include(x => x.Document)
            .Where(x =>
                x.Document.ProductId == productId
                && x.Document.Status == DocumentIngestionStatus.Completed
            )
            .OrderByDescending(x => seeds.Contains(x.Id))
            .ThenByDescending(x => x.Document.CreatedAt)
            .ThenBy(x => x.DocumentId)
            .ThenBy(x => x.Ordinal)
            .Take(Math.Clamp(limit, 1, 500))
            .ToArrayAsync(ct);
    }

    public Task<bool> ExistsByHashAsync(Guid productId, string hash, CancellationToken ct) =>
        db.KnowledgeDocuments.AnyAsync(x => x.ProductId == productId && x.Sha256 == hash, ct);

    public void Remove(KnowledgeDocument document) => db.KnowledgeDocuments.Remove(document);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
