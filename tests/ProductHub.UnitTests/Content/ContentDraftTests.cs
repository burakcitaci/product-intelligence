using ProductHub.Domain.Content;

namespace ProductHub.UnitTests.Content;

public sealed class ContentDraftTests
{
    [Theory]
    [InlineData(6)]
    public void Create_RejectsHighlightCountOutsideBounds(int count) =>
        Assert.Throws<ArgumentException>(() =>
            ContentDraft.Create(
                Guid.NewGuid(),
                "de",
                "Title",
                "Short",
                "Long",
                Enumerable.Range(1, count).Select(x => $"Highlight {x}"),
                Guid.NewGuid()
            )
        );

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SparseDraft_DoesNotRequireInventedHighlights(int count)
    {
        var draft = ContentDraft.Create(
            Guid.NewGuid(),
            "en",
            "Bottle",
            "Acme Bottle",
            "Acme Bottle",
            Enumerable.Range(0, count).Select(i => $"Fact {i}"),
            Guid.NewGuid()
        );
        Assert.Equal(count, draft.Highlights.Count);
        draft.UpdateContent("Bottle", "Acme Bottle", "Acme Bottle", []);
        Assert.Empty(draft.Highlights);
    }

    [Fact]
    public void Create_IsAlwaysPendingReview()
    {
        var draft = ContentDraft.Create(
            Guid.NewGuid(),
            "en",
            "Title",
            "Short",
            "Long",
            ["One", "Two", "Three"],
            Guid.NewGuid()
        );
        Assert.Equal(ContentDraftStatus.PendingReview, draft.Status);
    }

    [Fact]
    public void ApprovedDraft_CanBeRejected()
    {
        var draft = ContentDraft.Create(
            Guid.NewGuid(),
            "de",
            "Title",
            "Short",
            "Long",
            ["One", "Two", "Three"],
            Guid.NewGuid()
        );
        draft.Approve();
        Assert.Equal(ContentDraftStatus.Approved, draft.Status);
        draft.MarkCurrent();
        draft.Reject();
        Assert.Equal(ContentDraftStatus.Rejected, draft.Status);
        Assert.False(draft.IsCurrent);
        Assert.Throws<InvalidOperationException>(draft.Reject);
    }

    [Fact]
    public void MarkCurrent_RequiresApprovedDraft()
    {
        var draft = ContentDraft.Create(
            Guid.NewGuid(),
            "en",
            "Title",
            "Short",
            "Long",
            ["One", "Two", "Three"],
            Guid.NewGuid()
        );

        Assert.Throws<InvalidOperationException>(draft.MarkCurrent);
        draft.Approve();
        draft.MarkCurrent();
        Assert.True(draft.IsCurrent);
        draft.UnmarkCurrent();
        Assert.False(draft.IsCurrent);
    }

    [Fact]
    public void UpdateContent_ChangesPendingDraftButNotReviewedDraft()
    {
        var draft = ContentDraft.Create(
            Guid.NewGuid(),
            "en",
            "Old",
            "Old short",
            "Old long",
            ["One", "Two", "Three"],
            Guid.NewGuid()
        );
        draft.UpdateContent("New", "New short", "New long", ["First", "Second", "Third"]);
        Assert.Equal("New", draft.Title);
        Assert.Equal("First", draft.Highlights[0]);
        draft.Approve();
        Assert.Throws<InvalidOperationException>(() =>
            draft.UpdateContent("Again", "Short", "Long", ["1", "2", "3"])
        );
    }
}
