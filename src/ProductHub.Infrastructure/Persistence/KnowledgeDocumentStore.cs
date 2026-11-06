using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ProductHub.Application.Abstractions;
using ProductHub.Application.Products;
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
                && x.Document.SupersededByDocumentId == null
            )
            .OrderByDescending(x => seeds.Contains(x.Id))
            .ThenByDescending(x => x.Document.CreatedAt)
            .ThenBy(x => x.DocumentId)
            .ThenBy(x => x.Ordinal)
            .Take(Math.Clamp(limit, 1, 500))
            .ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<DocumentChunk>> SearchLexicalChunksAsync(
        Guid? productId,
        IReadOnlyList<string> terms,
        int limit,
        CancellationToken ct
    )
    {
        var words = terms.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().Take(24).ToArray();
        if (words.Length == 0)
            return [];
        // Parameterized LINQ expressions translate on SQL Server and SQLite. Contains escapes
        // wildcard characters; rank in SQL before limiting, independently of vector results.
        var chunk = Expression.Parameter(typeof(DocumentChunk), "chunk");
        var text = Expression.Call(
            Expression.Property(chunk, nameof(DocumentChunk.Text)),
            nameof(string.ToLower),
            Type.EmptyTypes
        );
        Expression score = Expression.Constant(0);
        foreach (var word in words)
        {
            var contains = Expression.Call(
                text,
                nameof(string.Contains),
                Type.EmptyTypes,
                Expression.Constant(word.ToLowerInvariant())
            );
            score = Expression.Add(
                score,
                Expression.Condition(contains, Expression.Constant(1), Expression.Constant(0))
            );
        }
        var relevance = Expression.Lambda<Func<DocumentChunk, int>>(score, chunk);
        var matches = Expression.Lambda<Func<DocumentChunk, bool>>(
            Expression.GreaterThan(score, Expression.Constant(0)),
            chunk
        );
        return await db
            .DocumentChunks.AsNoTracking()
            .Include(x => x.Document)
            .Where(x =>
                x.Document.Status == DocumentIngestionStatus.Completed
                && x.Document.SupersededByDocumentId == null
                && (productId == null || x.Document.ProductId == productId)
            )
            .Where(matches)
            .OrderByDescending(relevance)
            .ThenBy(x => x.Id)
            .Take(Math.Clamp(limit, 1, 80))
            .ToArrayAsync(ct);
    }

    public Task<bool> ExistsByHashAsync(Guid productId, string hash, CancellationToken ct) =>
        db.KnowledgeDocuments.AnyAsync(x => x.ProductId == productId && x.Sha256 == hash, ct);

    public void Remove(KnowledgeDocument document) => db.KnowledgeDocuments.Remove(document);

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The document revision changed. Refresh before retrying.");
        }
    }
}
