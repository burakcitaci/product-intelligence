namespace ProductHub.Domain.Content;

public enum ContentDraftStatus
{
    PendingReview,
    Approved,
    Rejected,
}

public sealed class ContentDraft
{
    private ContentDraft() { }

    private ContentDraft(
        Guid id,
        Guid productId,
        string language,
        string title,
        string shortDescription,
        string longDescription,
        IReadOnlyCollection<string> highlights,
        Guid generationId,
        DateTimeOffset createdAt
    )
    {
        Id = id;
        ProductId = productId;
        Language = Required(language, nameof(language), 10);
        Title = Required(title, nameof(title), 200);
        ShortDescription = Required(shortDescription, nameof(shortDescription), 500);
        LongDescription = Required(longDescription, nameof(longDescription), 8000);
        Highlights = highlights.Select(x => Required(x, nameof(highlights), 300)).ToList();
        if (Highlights.Count is < 3 or > 5)
            throw new ArgumentException(
                "Three to five highlights are required.",
                nameof(highlights)
            );
        GenerationId = generationId;
        CreatedAt = createdAt;
        Status = ContentDraftStatus.PendingReview;
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public string Language { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string ShortDescription { get; private set; } = null!;
    public string LongDescription { get; private set; } = null!;
    public List<string> Highlights { get; private set; } = [];
    public ContentDraftStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid GenerationId { get; private set; }
    public bool IsCurrent { get; private set; }

    public static ContentDraft Create(
        Guid productId,
        string language,
        string title,
        string shortDescription,
        string longDescription,
        IEnumerable<string> highlights,
        Guid generationId,
        DateTimeOffset? now = null
    ) =>
        new(
            Guid.NewGuid(),
            productId,
            language,
            title,
            shortDescription,
            longDescription,
            highlights.ToArray(),
            generationId,
            now ?? DateTimeOffset.UtcNow
        );

    public void Approve()
    {
        if (Status != ContentDraftStatus.PendingReview)
            throw new InvalidOperationException("Only pending drafts can be approved.");
        Status = ContentDraftStatus.Approved;
    }

    public void UpdateContent(
        string title,
        string shortDescription,
        string longDescription,
        IEnumerable<string> highlights
    )
    {
        if (Status != ContentDraftStatus.PendingReview)
            throw new InvalidOperationException("Only pending drafts can be edited.");
        var materialized = highlights.Select(x => Required(x, nameof(highlights), 300)).ToList();
        if (materialized.Count is < 3 or > 5)
            throw new ArgumentException(
                "Three to five highlights are required.",
                nameof(highlights)
            );
        Title = Required(title, nameof(title), 200);
        ShortDescription = Required(shortDescription, nameof(shortDescription), 500);
        LongDescription = Required(longDescription, nameof(longDescription), 8000);
        Highlights = materialized;
    }

    public void Reject()
    {
        if (Status is not (ContentDraftStatus.PendingReview or ContentDraftStatus.Approved))
            throw new InvalidOperationException("Only pending or approved drafts can be rejected.");
        Status = ContentDraftStatus.Rejected;
        IsCurrent = false;
    }

    public void MarkCurrent()
    {
        if (Status != ContentDraftStatus.Approved)
            throw new InvalidOperationException("Only approved drafts can be current.");
        IsCurrent = true;
    }

    public void UnmarkCurrent() => IsCurrent = false;

    private static string Required(string value, string parameter, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        value = value.Trim();
        if (value.Length > maxLength)
            throw new ArgumentOutOfRangeException(parameter, $"Maximum length is {maxLength}.");
        return value;
    }
}
