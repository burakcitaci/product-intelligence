using ProductHub.Application.Abstractions;
using ProductHub.Application.Products;

namespace ProductHub.Application.Knowledge;

public sealed class KnowledgeSearchService(
    IKnowledgeDocumentStore store,
    ITextEmbeddingGenerator embeddings,
    IVectorStore vectors
)
{
    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        SearchKnowledgeQuery query,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(query.Query) || query.Query.Length > 2000)
            throw new ValidationException(
                new Dictionary<string, string[]>
                {
                    ["query"] = ["Query is required and must not exceed 2000 characters."],
                }
            );
        if (query.Limit is < 1 or > 20)
            throw new ValidationException(
                new Dictionary<string, string[]> { ["limit"] = ["Limit must be between 1 and 20."] }
            );
        var vector = (
            await embeddings.GenerateAsync([query.Query.Trim()], cancellationToken)
        ).Single();
        var hits = await vectors.SearchAsync(
            vector,
            query.Limit,
            query.ProductId,
            cancellationToken
        );
        var chunks = await store.GetChunksAsync(hits.Select(x => x.Id), cancellationToken);
        return hits.Where(x => chunks.ContainsKey(x.Id))
            .Select(hit =>
            {
                var chunk = chunks[hit.Id];
                return new KnowledgeSearchResult(
                    chunk.DocumentId,
                    chunk.Document.FileName,
                    chunk.Id,
                    chunk.Ordinal,
                    chunk.Text,
                    hit.Score,
                    $"{chunk.Document.FileName}#chunk-{chunk.Ordinal}"
                );
            })
            .ToArray();
    }
}
