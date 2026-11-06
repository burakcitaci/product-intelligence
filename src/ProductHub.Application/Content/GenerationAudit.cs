using ProductHub.Application.Products;

namespace ProductHub.Application.Content;

public sealed record GenerationAuditEntryDto(
    Guid Id,
    Guid? GenerationId,
    Guid? JobId,
    Guid? DraftId,
    string Language,
    string? Model,
    string RetrievalMode,
    bool? IncludeFacts,
    int? RequestedTopK,
    string Status,
    string Stage,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMs,
    string? ErrorCode,
    string? ErrorMessage,
    AiGenerationMetadataDto? Metadata
);

public interface IGenerationAuditReader
{
    Task<PagedResult<GenerationAuditEntryDto>> ReadAsync(
        Guid productId,
        int page,
        int pageSize,
        string? status,
        string? language,
        CancellationToken ct
    );
}
