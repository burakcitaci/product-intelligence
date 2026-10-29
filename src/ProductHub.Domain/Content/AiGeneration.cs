namespace ProductHub.Domain.Content;

public enum AiGenerationStatus
{
    InProgress,
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
        string correlationId,
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
        string correlationId,
        DateTimeOffset? now = null
    ) =>
        new(
            Guid.NewGuid(),
            productId,
            idempotencyKey,
            provider,
            model,
            promptVersion,
            language,
            correlationId,
            now ?? DateTimeOffset.UtcNow
        );

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
