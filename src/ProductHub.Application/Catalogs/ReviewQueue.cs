using ProductHub.Application.Products;

namespace ProductHub.Application.Catalogs;

public sealed record ReviewQueueRow(
    ProductDto Product,
    int PendingCount,
    bool MissingLanguage,
    bool MissingFacts,
    bool StaleContent
);

public sealed record ReviewQueueCounts(
    int All,
    int Pending,
    int MissingLanguage,
    int MissingFacts,
    int Stale
);

public sealed record ReviewQueueResult(
    IReadOnlyList<ReviewQueueRow> Items,
    ReviewQueueCounts Counts,
    int TotalCount,
    int Page,
    int PageSize
)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class ReviewQueue
{
    public static ReviewQueueResult Select(
        IReadOnlyList<ReviewQueueRow> rows,
        string filter,
        int page,
        int pageSize
    )
    {
        var counts = new ReviewQueueCounts(
            rows.Count,
            rows.Count(x => x.PendingCount > 0),
            rows.Count(x => x.MissingLanguage),
            rows.Count(x => x.MissingFacts),
            rows.Count(x => x.StaleContent)
        );
        var filtered = rows.Where(x =>
                filter switch
                {
                    "pending" => x.PendingCount > 0,
                    "missing-language" => x.MissingLanguage,
                    "missing-facts" => x.MissingFacts,
                    "stale" => x.StaleContent,
                    _ => true,
                }
            )
            .ToArray();
        // Clamp a saved page when filtering or server changes shrink the queue.
        var currentPage = Math.Min(
            page,
            Math.Max(1, (int)Math.Ceiling(filtered.Length / (double)pageSize))
        );
        return new(
            filtered.Skip((currentPage - 1) * pageSize).Take(pageSize).ToArray(),
            counts,
            filtered.Length,
            currentPage,
            pageSize
        );
    }
}
