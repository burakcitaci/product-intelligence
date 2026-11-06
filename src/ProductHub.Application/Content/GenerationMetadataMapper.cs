using System.Text.Json;
using ProductHub.Domain.Content;

namespace ProductHub.Application.Content;

public static class GenerationMetadataMapper
{
    public static AiGenerationMetadataDto Map(AiGeneration generation) =>
        new(
            generation.Id,
            generation.Provider,
            generation.Model,
            generation.PromptVersion,
            generation.StartedAt,
            generation.CompletedAt,
            generation.DurationMs,
            generation.InputTokens,
            generation.OutputTokens,
            generation.Status.ToString(),
            generation.ErrorCode,
            generation.CorrelationId,
            generation.GroundingMode.ToString(),
            generation.RequestedTopK,
            generation.RetrievalStatus.ToString(),
            generation.RetrievalDurationMs,
            generation.RetrievedChunkCount,
            generation.RetrievedDocumentCount,
            DeserializeSources(generation.RetrievedSourcesJson),
            generation.RetrievalErrorCode,
            generation.IncludeFacts,
            generation.GroundingMode switch
            {
                GroundingMode.GraphRag => "Graph",
                GroundingMode.AdvancedRag => "Advanced",
                GroundingMode.Rag => "Naive",
                _ => "None",
            },
            generation.GenerationParametersJson is null
                ? null
                : JsonSerializer.Deserialize<GenerationParametersDto>(
                    generation.GenerationParametersJson
                )
        );

    private static IReadOnlyList<RetrievalSourceDto> DeserializeSources(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<RetrievalSourceDto[]>(json) ?? [];
}
