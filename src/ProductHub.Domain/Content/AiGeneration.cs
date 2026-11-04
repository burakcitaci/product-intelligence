namespace ProductHub.Domain.Content;

public enum AiGenerationStatus
{
    InProgress,
    Succeeded,
    Failed,
}

public enum GroundingMode
{
    FactsOnly,
    Rag,
    AdvancedRag,
    GraphRag,
    IdentityOnly,
}

public enum RetrievalStatus
{
    NotRequested,
    Succeeded,
    Failed,
}

public sealed class AiGeneration
{
    private AiGeneration() { }

    private AiGeneration(
        Guid id,
        Guid productId,
        string idempotencyKey,
        string provider,
        string model,
        string promptVersion,
        string language,
        GroundingMode groundingMode,
        string correlationId,
        int? requestedTopK,
        DateTimeOffset startedAt
    )
    {
        Id = id;
        ProductId = productId;
        IdempotencyKey = Required(idempotencyKey, nameof(idempotencyKey), 100);
        Provider = Required(provider, nameof(provider), 50);
        Model = Required(model, nameof(model), 100);
        PromptVersion = Required(promptVersion, nameof(promptVersion), 100);
        Language = Required(language, nameof(language), 10);
        GroundingMode = groundingMode;
        RequestedTopK = groundingMode
            is GroundingMode.Rag
                or GroundingMode.AdvancedRag
                or GroundingMode.GraphRag
            ? requestedTopK
            : null;
        RetrievalStatus = RetrievalStatus.NotRequested;
        CorrelationId = Required(correlationId, nameof(correlationId), 100);
        StartedAt = startedAt;
        Status = AiGenerationStatus.InProgress;
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string Provider { get; private set; } = null!;
    public string Model { get; private set; } = null!;
    public string PromptVersion { get; private set; } = null!;
    public string Language { get; private set; } = null!;
    public GroundingMode GroundingMode { get; private set; }
    public int? RequestedTopK { get; private set; }
    public bool IncludeFacts { get; private set; } = true;
    public RetrievalStatus RetrievalStatus { get; private set; }
    public long? RetrievalDurationMs { get; private set; }
    public int RetrievedChunkCount { get; private set; }
    public int RetrievedDocumentCount { get; private set; }
    public string? RetrievedSourcesJson { get; private set; }
    public string? RetrievalErrorCode { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public long? DurationMs { get; private set; }
    public int? InputTokens { get; private set; }
    public int? OutputTokens { get; private set; }
    public AiGenerationStatus Status { get; private set; }
    public string? ErrorCode { get; private set; }
    public string CorrelationId { get; private set; } = null!;

    public static AiGeneration Start(
        Guid productId,
        string idempotencyKey,
        string provider,
        string model,
        string promptVersion,
        string language,
        GroundingMode groundingMode,
        string correlationId,
        int? requestedTopK = null,
        DateTimeOffset? now = null,
        bool includeFacts = true
    ) =>
        new(
            Guid.NewGuid(),
            productId,
            idempotencyKey,
            provider,
            model,
            promptVersion,
            language,
            groundingMode,
            correlationId,
            requestedTopK,
            now ?? DateTimeOffset.UtcNow
        )
        {
            IncludeFacts = includeFacts,
        };

    public void RecordRetrieval(
        long durationMs,
        int chunkCount,
        int documentCount,
        string sourcesJson
    )
    {
        if (
            GroundingMode
            is not (GroundingMode.Rag or GroundingMode.AdvancedRag or GroundingMode.GraphRag)
        )
            throw new InvalidOperationException("Retrieval is only valid for RAG generations.");
        RetrievalStatus = RetrievalStatus.Succeeded;
        RetrievalDurationMs = Math.Max(0, durationMs);
        RetrievedChunkCount = Math.Max(0, chunkCount);
        RetrievedDocumentCount = Math.Max(0, documentCount);
        RetrievedSourcesJson = sourcesJson;
        RetrievalErrorCode = null;
    }

    public void RecordRetrievalFailure(long durationMs, string errorCode)
    {
        RetrievalStatus = RetrievalStatus.Failed;
        RetrievalDurationMs = Math.Max(0, durationMs);
        RetrievalErrorCode = Required(errorCode, nameof(errorCode), 100);
    }

    public void Complete(int? inputTokens, int? outputTokens, DateTimeOffset? now = null)
    {
        CompletedAt = now ?? DateTimeOffset.UtcNow;
        DurationMs = Math.Max(0, (long)(CompletedAt.Value - StartedAt).TotalMilliseconds);
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        Status = AiGenerationStatus.Succeeded;
        ErrorCode = null;
    }

    public void Fail(string errorCode, DateTimeOffset? now = null)
    {
        CompletedAt = now ?? DateTimeOffset.UtcNow;
        DurationMs = Math.Max(0, (long)(CompletedAt.Value - StartedAt).TotalMilliseconds);
        Status = AiGenerationStatus.Failed;
        ErrorCode = Required(errorCode, nameof(errorCode), 100);
    }

    private static string Required(string value, string parameter, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        value = value.Trim();
        if (value.Length > maxLength)
            throw new ArgumentOutOfRangeException(parameter, $"Maximum length is {maxLength}.");
        return value;
    }
}
