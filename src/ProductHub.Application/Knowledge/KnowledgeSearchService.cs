using System.Text.RegularExpressions;
using ProductHub.Application.Abstractions;
using ProductHub.Application.Products;
using ProductHub.Domain.Knowledge;

namespace ProductHub.Application.Knowledge;

public sealed class KnowledgeSearchService(
    IKnowledgeDocumentStore store,
    ITextEmbeddingGenerator embeddings,
    IVectorStore vectors
)
{
    private const int MaxCandidatesPerQuery = 80;
    private const int MaxContextCharacters = 12000;
    private const int MaxExcerptCharacters = 2400;

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
        if (
            query.Mode is not (RetrievalMode.Naive or RetrievalMode.Advanced or RetrievalMode.Graph)
        )
            throw new ValidationException(
                new Dictionary<string, string[]>
                {
                    ["mode"] = ["Knowledge search requires Naive, Advanced, or Graph retrieval."],
                }
            );

        if (
            query.Mode == RetrievalMode.Graph
            && (query.ProductId is null || query.ProductId == Guid.Empty)
        )
            throw new ValidationException(
                new Dictionary<string, string[]>
                {
                    ["productId"] = ["Graph retrieval requires a product id."],
                }
            );
        var queries = query.Mode is RetrievalMode.Advanced or RetrievalMode.Graph
            ? ExpandQueries(query.Query.Trim())
            : new[] { query.Query.Trim() };
        var queryVectors = await embeddings.GenerateAsync(queries, cancellationToken);
        if (queryVectors.Count != queries.Length)
            throw new InvalidOperationException("Embedding count did not match query count.");

        var rankings = new List<IReadOnlyList<VectorSearchHit>>();
        // Sequential calls keep concurrency bounded and honor cancellation between retrievals.
        foreach (var vector in queryVectors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rankings.Add(
                await vectors.SearchAsync(
                    vector,
                    query.Mode is RetrievalMode.Advanced or RetrievalMode.Graph
                        ? Math.Min(query.Limit * 4, MaxCandidatesPerQuery)
                        : query.Limit,
                    query.ProductId,
                    cancellationToken
                )
            );
        }
        var chunks = await store.GetChunksAsync(
            rankings.SelectMany(x => x).Select(x => x.Id).Distinct(),
            cancellationToken
        );
        // SQL remains authoritative: discard stale, incomplete, or incorrectly scoped vector hits.
        var candidates = chunks
            .Values.Where(x =>
                x.Document.Status == DocumentIngestionStatus.Completed
                && (query.ProductId is null || x.Document.ProductId == query.ProductId)
            )
            .ToDictionary(x => x.Id);

        if (query.Mode == RetrievalMode.Naive)
            return rankings[0]
                .Where(x => candidates.ContainsKey(x.Id))
                .DistinctBy(x => x.Id)
                .Select(hit => Map(candidates[hit.Id], hit.Score))
                .ToArray();

        if (query.Mode == RetrievalMode.Graph)
        {
            var seeds = RankAdvanced(
                query with
                {
                    Limit = Math.Max(query.Limit, 8),
                },
                rankings,
                candidates,
                cancellationToken
            );
            if (seeds.Count == 0)
                return [];
            var corpus = await store.GetGraphChunksAsync(
                query.ProductId!.Value,
                seeds.Select(x => x.ChunkId).ToArray(),
                500,
                cancellationToken
            );
            return KnowledgeGraphSearch.Retrieve(query, seeds, corpus, cancellationToken);
        }
        return RankAdvanced(query, rankings, candidates, cancellationToken);
    }

    private static string[] ExpandQueries(string query)
    {
        var parts = query.Split(
            '|',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
        );
        // Use supplied terms only. Product queries contain SKU | name | brand | optional facts.
        return new[]
        {
            query,
            string.Join(" ", parts.Take(3)),
            string.Join(" ", parts.Skip(1).Take(2)),
        }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
    }

    private static IReadOnlyList<KnowledgeSearchResult> RankAdvanced(
        SearchKnowledgeQuery query,
        IReadOnlyList<IReadOnlyList<VectorSearchHit>> rankings,
        IReadOnlyDictionary<Guid, DocumentChunk> candidates,
        CancellationToken cancellationToken
    )
    {
        if (candidates.Count == 0)
            return [];
        const double rankConstant = 60;
        var scores = candidates.Keys.ToDictionary(x => x, _ => 0d);
        foreach (var ranking in rankings)
        foreach (
            var (hit, index) in ranking.DistinctBy(x => x.Id).Select((hit, index) => (hit, index))
        )
            if (scores.ContainsKey(hit.Id))
                scores[hit.Id] += 1d / (rankConstant + index + 1);

        var tokens = candidates.ToDictionary(x => x.Key, x => Tokenize(x.Value.Text));
        var queryTerms = Tokenize(query.Query).Distinct().ToArray();
        var averageLength = Math.Max(1d, tokens.Values.Average(x => x.Length));
        var documentFrequency = queryTerms.ToDictionary(
            term => term,
            term => tokens.Values.Count(words => words.Contains(term))
        );
        // BM25 reranks the semantic candidate union; this is not a full-corpus keyword index.
        var lexical = candidates
            .Keys.Select(id =>
            {
                var words = tokens[id];
                var score = queryTerms.Sum(term =>
                {
                    var frequency = words.Count(x => x == term);
                    var inverseFrequency = Math.Log(
                        1
                            + (candidates.Count - documentFrequency[term] + .5)
                                / (documentFrequency[term] + .5)
                    );
                    return inverseFrequency
                        * frequency
                        * 2.2
                        / (frequency + 1.2 * (.25 + .75 * words.Length / averageLength));
                });
                return (Id: id, Score: score);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Id)
            .ToArray();
        for (var index = 0; index < lexical.Length; index++)
            scores[lexical[index].Id] += 1d / (rankConstant + index + 1);

        var maximumScore = scores.Values.Max();
        var remaining = scores.Keys.ToHashSet();
        var selected = new List<KnowledgeSearchResult>();
        var selectedTerms = new List<HashSet<string>>();
        var tokenSets = tokens.ToDictionary(x => x.Key, x => x.Value.ToHashSet());
        var remainingCharacters = MaxContextCharacters;
        while (remaining.Count > 0 && selected.Count < query.Limit && remainingCharacters > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = remaining
                .Select(id =>
                {
                    var similarity =
                        selectedTerms.Count == 0
                            ? 0
                            : selectedTerms.Max(words => Similarity(tokenSets[id], words));
                    return (
                        Id: id,
                        Similarity: similarity,
                        Score: .8 * scores[id] / maximumScore - .2 * similarity
                    );
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Id)
                .First();
            remaining.Remove(next.Id);
            if (next.Similarity >= .85)
                continue;
            var chunk = candidates[next.Id];
            var text = chunk.Text[
                ..Math.Min(chunk.Text.Length, Math.Min(MaxExcerptCharacters, remainingCharacters))
            ];
            selected.Add(Map(chunk, (float)(scores[next.Id] / maximumScore)) with { Text = text });
            selectedTerms.Add(tokenSets[next.Id]);
            remainingCharacters -= text.Length;
        }
        return selected;
    }

    private static string[] Tokenize(string text) =>
        Regex
            .Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)
            .Select(x => x.Value)
            .Where(x => x.Length > 1)
            .ToArray();

    private static double Similarity(HashSet<string> left, HashSet<string> right)
    {
        var intersection = left.Count(right.Contains);
        var union = left.Count + right.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static KnowledgeSearchResult Map(DocumentChunk chunk, float score) =>
        new(
            chunk.DocumentId,
            chunk.Document.FileName,
            chunk.Id,
            chunk.Ordinal,
            chunk.Text,
            score,
            $"{chunk.Document.FileName}#chunk-{chunk.Ordinal}"
        );
}
