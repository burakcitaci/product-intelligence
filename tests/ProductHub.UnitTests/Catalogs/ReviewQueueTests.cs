using ProductHub.Application.Catalogs;
using ProductHub.Application.Products;

namespace ProductHub.UnitTests.Catalogs;

public sealed class ReviewQueueTests
{
    private static ReviewQueueRow Row(
        string sku,
        int pending = 0,
        bool missingLanguage = false,
        bool missingFacts = false,
        bool stale = false
    ) =>
        new(
            new ProductDto(
                Guid.NewGuid(),
                sku,
                "Bottle",
                "Acme",
                null,
                null,
                [],
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                ""
            ),
            pending,
            missingLanguage,
            missingFacts,
            stale
        );

    [Fact]
    public void Filters_UseProductCountsAndKeepCatalogWideSummary()
    {
        ReviewQueueRow[] rows =
        [
            Row("A", pending: 3, missingLanguage: true, missingFacts: true),
            Row("B", stale: true),
            Row("C", missingLanguage: true),
        ];
        var pending = ReviewQueue.Select(rows, "pending", 1, 25);
        Assert.Equal("A", Assert.Single(pending.Items).Product.Sku);
        Assert.Equal(new ReviewQueueCounts(3, 1, 2, 1, 1), pending.Counts);
        Assert.Equal(2, ReviewQueue.Select(rows, "missing-language", 1, 25).TotalCount);
        Assert.Equal(
            "A",
            Assert.Single(ReviewQueue.Select(rows, "missing-facts", 1, 25).Items).Product.Sku
        );
        Assert.Equal(
            "B",
            Assert.Single(ReviewQueue.Select(rows, "stale", 1, 25).Items).Product.Sku
        );
    }

    [Fact]
    public void Counts_AreNotLimitedToTheFirstHundredProducts()
    {
        var rows = Enumerable
            .Range(0, 125)
            .Select(i => Row(i.ToString(), pending: i == 124 ? 1 : 0))
            .ToArray();
        var result = ReviewQueue.Select(rows, "pending", 1, 25);
        Assert.Equal(125, result.Counts.All);
        Assert.Equal("124", Assert.Single(result.Items).Product.Sku);
    }

    [Fact]
    public void SavedPages_ClampWhenQueueShrinks()
    {
        var rows = new[] { Row("A"), Row("B"), Row("C") };
        var page = ReviewQueue.Select(rows, "all", 2, 2);
        Assert.Equal("C", Assert.Single(page.Items).Product.Sku);
        var shrunk = ReviewQueue.Select(rows.Take(1).ToArray(), "all", 2, 2);
        Assert.Equal(1, shrunk.Page);
        Assert.Single(shrunk.Items);
        var empty = ReviewQueue.Select([], "stale", 8, 25);
        Assert.Empty(empty.Items);
        Assert.Equal(1, empty.Page);
        Assert.Equal(0, empty.TotalPages);
    }
}
