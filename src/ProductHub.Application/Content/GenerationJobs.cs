namespace ProductHub.Application.Content;

public sealed record GenerationJobDto(
    Guid Id,
    Guid ProductId,
    string Status,
    string Stage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool CancellationRequested,
    Guid? DraftId,
    string? ErrorCode,
    string? ErrorMessage,
    string? IdempotencyKey = null
);

public interface IGenerationJobService
{
    Task<GenerationJobDto> EnqueueAsync(
        GenerateDraftCommand command,
        string requestedBy,
        CancellationToken ct
    );
    Task<GenerationJobDto> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<GenerationJobDto>> ListAsync(Guid productId, CancellationToken ct);
    Task<GenerationJobDto> CancelAsync(Guid id, CancellationToken ct);
}
