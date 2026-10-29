namespace ProductHub.Domain.Knowledge;

public enum DocumentIngestionStatus
{
    Pending,
    Completed,
    Failed,
}

public sealed class KnowledgeDocument
{
    private readonly List<DocumentChunk> _chunks = [];

    private KnowledgeDocument() { }

    private KnowledgeDocument(Guid id, string fileName, string contentType, string sha256) =>
        (Id, FileName, ContentType, Sha256, Status, CreatedAt) = (
            id,
            fileName,
            contentType,
            sha256,
            DocumentIngestionStatus.Pending,
            DateTimeOffset.UtcNow
        );

    public Guid Id { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public string Sha256 { get; private set; } = "";
    public DocumentIngestionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? ErrorCode { get; private set; }
    public IReadOnlyCollection<DocumentChunk> Chunks => _chunks;

    public static KnowledgeDocument Start(string fileName, string contentType, string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        return new KnowledgeDocument(Guid.NewGuid(), fileName.Trim(), contentType.Trim(), sha256);
    }

    public void Complete(IEnumerable<string> chunks)
    {
        if (Status != DocumentIngestionStatus.Pending)
            throw new InvalidOperationException("Document ingestion is already terminal.");
        var values = chunks
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToArray();
        if (values.Length == 0)
            throw new ArgumentException("At least one document chunk is required.", nameof(chunks));
        for (var index = 0; index < values.Length; index++)
            _chunks.Add(DocumentChunk.Create(Id, index, values[index]));
        Status = DocumentIngestionStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string errorCode)
    {
        if (Status != DocumentIngestionStatus.Pending)
            return;
        Status = DocumentIngestionStatus.Failed;
        ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "ingestion-failed" : errorCode;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class DocumentChunk
{
    private DocumentChunk() { }

    private DocumentChunk(Guid id, Guid documentId, int ordinal, string text) =>
        (Id, DocumentId, Ordinal, Text) = (id, documentId, ordinal, text);

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public int Ordinal { get; private set; }
    public string Text { get; private set; } = "";
    public KnowledgeDocument Document { get; private set; } = null!;

    internal static DocumentChunk Create(Guid documentId, int ordinal, string text) =>
        new(Guid.NewGuid(), documentId, ordinal, text);
}
