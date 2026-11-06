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

    private KnowledgeDocument(
        Guid id,
        Guid productId,
        string fileName,
        string contentType,
        string sha256
    ) =>
        (Id, ProductId, FileName, ContentType, Sha256, Status, CreatedAt) = (
            id,
            productId,
            fileName,
            contentType,
            sha256,
            DocumentIngestionStatus.Pending,
            DateTimeOffset.UtcNow
        );

    public Guid Id { get; private set; }
    public Guid? ProductId { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public string Sha256 { get; private set; } = "";
    public DocumentIngestionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? ErrorCode { get; private set; }
    public string ExtractionVersion { get; private set; } = "legacy";
    public Guid? SupersededByDocumentId { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }

    public void RecordExtractionVersion(string version)
    {
        if (Status != DocumentIngestionStatus.Pending)
            throw new InvalidOperationException("Extraction is already terminal.");
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (version.Length > 100)
            throw new ArgumentException("Extraction version is too long.");
        ExtractionVersion = version;
    }

    public void SupersedeWith(KnowledgeDocument replacement)
    {
        if (SupersededByDocumentId == replacement.Id)
            return;
        if (
            Status != DocumentIngestionStatus.Completed
            || replacement.Status != DocumentIngestionStatus.Completed
            || ProductId != replacement.ProductId
            || replacement.Id == Id
            || SupersededByDocumentId is not null
            || replacement.SupersededByDocumentId is not null
            || replacement.CreatedAt <= CreatedAt
        )
            throw new InvalidOperationException(
                "Choose a newer completed active document for the same product."
            );
        SupersededByDocumentId = replacement.Id;
        SupersededAt = DateTimeOffset.UtcNow;
    }

    public IReadOnlyCollection<DocumentChunk> Chunks => _chunks;

    public static KnowledgeDocument Start(
        Guid productId,
        string fileName,
        string contentType,
        string sha256
    )
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("Product id is required.", nameof(productId));
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        return new KnowledgeDocument(
            Guid.NewGuid(),
            productId,
            fileName.Trim(),
            contentType.Trim(),
            sha256
        );
    }

    public void Complete(IEnumerable<string> chunks)
    {
        PrepareChunks(chunks);
        Complete();
    }

    public void PrepareChunks(IEnumerable<string> chunks)
    {
        if (Status != DocumentIngestionStatus.Pending)
            throw new InvalidOperationException("Document ingestion is already terminal.");
        if (_chunks.Count > 0)
            throw new InvalidOperationException("Chunks have already been prepared.");
        var values = chunks
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToArray();
        if (values.Length == 0)
            throw new ArgumentException("At least one document chunk is required.", nameof(chunks));
        for (var index = 0; index < values.Length; index++)
            _chunks.Add(DocumentChunk.Create(Id, index, values[index]));
    }

    public void Complete()
    {
        if (Status != DocumentIngestionStatus.Pending || _chunks.Count == 0)
            throw new InvalidOperationException(
                "Only a pending document with prepared chunks can complete."
            );
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
