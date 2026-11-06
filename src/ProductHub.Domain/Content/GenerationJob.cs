namespace ProductHub.Domain.Content;

/// <summary>Durable queue identity; the worker uses conditional updates for lease fencing.</summary>
public sealed class GenerationJob
{
    private GenerationJob() { }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public string IdempotencyKey { get; private set; } = "";
    public string CommandJson { get; private set; } = "";
    public string RequestedBy { get; private set; } = "";
    public string Status { get; private set; } = "Queued";
    public string Stage { get; private set; } = "Queued";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public bool CancellationRequested { get; private set; }
    public Guid? DraftId { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    public static GenerationJob Queue(
        Guid productId,
        string key,
        string commandJson,
        string requestedBy
    )
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("Product is required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        if (key.Length > 100)
            throw new ArgumentException("Idempotency key must be at most 100 characters.");
        return new()
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            IdempotencyKey = key,
            CommandJson = commandJson,
            RequestedBy = requestedBy,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }
}
