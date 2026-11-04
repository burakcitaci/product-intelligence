using System.Text.RegularExpressions;
using ProductHub.Domain.Knowledge;

namespace ProductHub.Application.Knowledge;

/// <summary>A bounded, request-local chunk/concept graph over one product's completed documents.</summary>
internal static class KnowledgeGraphSearch
{
    private const int MaxNodes = 500;
    private const int MaxNeighbors = 12;
    private const int MaxHops = 2;
    private const int MaxExcerptCharacters = 2400;
    private const int MaxContextCharacters = 12000;
    private static readonly HashSet<string> StopWords = new(
        (
            "this that these those with from into your their which when then than "
            + "have has been were will would should could must only also each more "
            + "product products manual instructions information please before after "
            + "diese dieser dieses einen einem einer eine oder aber nicht sind wird "
            + "werden durch beim eine auch nach sich kann können müssen sowie"
        ).Split(' '),
        StringComparer.Ordinal
    );

    public static IReadOnlyList<KnowledgeSearchResult> Retrieve(
        SearchKnowledgeQuery query,
        IReadOnlyList<KnowledgeSearchResult> semanticSeeds,
        IReadOnlyList<DocumentChunk> corpus,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        // Revalidate the SQL projection; a graph never crosses product or ingestion boundaries.
        var nodes = corpus
            .Where(x =>
                x.Document.ProductId == query.ProductId
                && x.Document.Status == DocumentIngestionStatus.Completed
            )
            .DistinctBy(x => x.Id)
            .Take(MaxNodes)
            .ToDictionary(x => x.Id);
        var seeds = semanticSeeds.Where(x => nodes.ContainsKey(x.ChunkId)).ToArray();
        if (seeds.Length == 0)
            return [];

        var terms = nodes.ToDictionary(x => x.Key, x => Terms(x.Value.Text));
        var postings = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        foreach (var (id, words) in terms)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var word in words)
            {
                if (!postings.TryGetValue(word, out var ids))
                    postings[word] = ids = [];
                ids.Add(id);
            }
        }
        // Suppress hubs and singletons: only shared, relatively rare concepts create edges.
        var maximumFrequency = Math.Min(16, Math.Max(2, nodes.Count * 2 / 5));
        var concepts = terms.ToDictionary(
            x => x.Key,
            x =>
                x.Value.Where(term =>
                        postings[term].Count >= 2 && postings[term].Count <= maximumFrequency
                    )
                    .OrderBy(term => postings[term].Count)
                    .ThenBy(term => term, StringComparer.Ordinal)
                    .Take(24)
                    .ToHashSet(StringComparer.Ordinal)
        );
        var ordinalIndex = nodes.Values.ToDictionary(x => (x.DocumentId, x.Ordinal));
        var edges = new Dictionary<Guid, Edge[]>();
        foreach (var (id, chunk) in nodes)
        {
            ct.ThrowIfCancellationRequested();
            var shared = new Dictionary<Guid, List<string>>();
            foreach (var concept in concepts[id])
            foreach (var neighbor in postings[concept])
            {
                if (neighbor == id || !concepts[neighbor].Contains(concept))
                    continue;
                if (!shared.TryGetValue(neighbor, out var labels))
                    shared[neighbor] = labels = [];
                labels.Add(concept);
            }
            var neighbors = shared
                .Select(pair => new Edge(
                    pair.Key,
                    .7 + Math.Min(.3, pair.Value.Count * .05),
                    $"shared-concept:{pair.Value.Order(StringComparer.Ordinal).First()}"
                ))
                .ToList();
            foreach (var ordinal in new[] { chunk.Ordinal - 1, chunk.Ordinal + 1 })
                if (ordinalIndex.TryGetValue((chunk.DocumentId, ordinal), out var adjacent))
                    neighbors.Add(new(adjacent.Id, .75, "adjacent-chunk"));
            edges[id] = neighbors
                .OrderByDescending(x => x.Weight)
                .ThenBy(x => x.Target)
                .DistinctBy(x => x.Target)
                .Take(MaxNeighbors)
                .ToArray();
        }

        // Reserve half the output for semantic anchors; expand only from those returned anchors.
        var anchorCount = Math.Min(8, Math.Max(1, (query.Limit + 1) / 2));
        var anchors = seeds.Take(anchorCount).ToArray();
        var routes = anchors.ToDictionary(
            x => x.ChunkId,
            x => new Route(x.ChunkId, x.Score, 0, [Citation(nodes[x.ChunkId])], [x.ChunkId])
        );
        var queue = new Queue<Route>(routes.Values.OrderByDescending(x => x.Score));
        while (queue.TryDequeue(out var route))
        {
            ct.ThrowIfCancellationRequested();
            if (route.Hops >= MaxHops || routes[route.Id].Score > route.Score)
                continue;
            foreach (var edge in edges[route.Id])
            {
                if (route.Visited.Contains(edge.Target))
                    continue;
                var score = route.Score * .65 * edge.Weight;
                if (routes.TryGetValue(edge.Target, out var existing) && existing.Score >= score)
                    continue;
                var next = new Route(
                    edge.Target,
                    score,
                    route.Hops + 1,
                    [.. route.Path, edge.Relation, Citation(nodes[edge.Target])],
                    [.. route.Visited, edge.Target]
                );
                routes[edge.Target] = next;
                queue.Enqueue(next);
            }
        }

        var ordered = anchors
            .Select(x => routes[x.ChunkId])
            .Concat(
                routes
                    .Values.Where(x => x.Hops > 0)
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Id)
            )
            .Concat(
                seeds
                    .Skip(anchorCount)
                    .Select(x => new Route(
                        x.ChunkId,
                        x.Score,
                        0,
                        [Citation(nodes[x.ChunkId])],
                        [x.ChunkId]
                    ))
            );
        var selected = new List<KnowledgeSearchResult>();
        var selectedIds = new HashSet<Guid>();
        var selectedTerms = new List<HashSet<string>>();
        var charactersLeft = MaxContextCharacters;
        foreach (var route in ordered)
        {
            ct.ThrowIfCancellationRequested();
            if (selected.Count >= query.Limit || charactersLeft == 0)
                break;
            if (!selectedIds.Add(route.Id))
                continue;
            var words = terms[route.Id];
            if (selectedTerms.Any(previous => Similarity(words, previous) >= .85))
                continue;
            var chunk = nodes[route.Id];
            var text = chunk.Text[
                ..Math.Min(chunk.Text.Length, Math.Min(MaxExcerptCharacters, charactersLeft))
            ];
            selected.Add(
                new(
                    chunk.DocumentId,
                    chunk.Document.FileName,
                    chunk.Id,
                    chunk.Ordinal,
                    text,
                    (float)route.Score,
                    Citation(chunk),
                    route.Hops,
                    route.Path
                )
            );
            selectedTerms.Add(words);
            charactersLeft -= text.Length;
        }
        return selected;
    }

    private static HashSet<string> Terms(string text) =>
        Regex
            .Matches(
                text[..Math.Min(text.Length, MaxExcerptCharacters)].ToLowerInvariant(),
                @"[\p{L}\p{N}][\p{L}\p{N}-]{3,39}",
                RegexOptions.CultureInvariant
            )
            .Select(x => x.Value)
            .Where(x => !StopWords.Contains(x))
            .ToHashSet(StringComparer.Ordinal);

    private static double Similarity(HashSet<string> left, HashSet<string> right)
    {
        var intersection = left.Count(right.Contains);
        var union = left.Count + right.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static string Citation(DocumentChunk chunk) =>
        $"{chunk.Document.FileName}#chunk-{chunk.Ordinal}";

    private sealed record Edge(Guid Target, double Weight, string Relation);

    private sealed record Route(
        Guid Id,
        double Score,
        int Hops,
        IReadOnlyList<string> Path,
        IReadOnlyList<Guid> Visited
    );
}
