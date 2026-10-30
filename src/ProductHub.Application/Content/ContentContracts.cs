using ProductHub.Domain.Content;

namespace ProductHub.Application.Content;

public sealed record GenerateDraftCommand(
    Guid ProductId,
    string Language,
    string IdempotencyKey,
    string CorrelationId,
    bool UseRag = false,
    int TopK = 5,
    string? Model = null
);

public sealed record UpdateDraftCommand(
    string Title,
    string ShortDescription,
    string LongDescription,
    IReadOnlyList<string> Highlights
);

public sealed record ContentDraftDto(
    Guid Id,
    Guid ProductId,
    string Language,
    string Title,
    string ShortDescription,
    string LongDescription,
    IReadOnlyList<string> Highlights,
    ContentDraftStatus Status,
    bool IsCurrent,
    DateTimeOffset CreatedAt,
    Guid GenerationId
);

public sealed record AiGenerationMetadataDto(
    Guid Id,
    string Provider,
    string Model,
    string PromptVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMs,
    int? InputTokens,
    int? OutputTokens,
    string Status,
    string? ErrorCode,
    string CorrelationId,
    string GroundingMode,
    int? RequestedTopK,
    string RetrievalStatus,
    long? RetrievalDurationMs,
    int RetrievedChunkCount,
    int RetrievedDocumentCount,
    IReadOnlyList<RetrievalSourceDto> RetrievalSources,
    string? RetrievalErrorCode
);

public sealed record RetrievalSourceDto(
    Guid DocumentId,
    string FileName,
    string Citation,
    float Score
);

public sealed class NotFoundException(string message) : Exception(message);

public sealed class GenerationUnavailableException(
    string errorCode,
    string message,
    Exception? innerException = null
) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
