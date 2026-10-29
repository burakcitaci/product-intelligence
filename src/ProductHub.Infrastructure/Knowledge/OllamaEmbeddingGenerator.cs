using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using ProductHub.Application.Knowledge;

namespace ProductHub.Infrastructure.Knowledge;

internal sealed class OllamaEmbeddingGenerator(HttpClient client, IOptions<RagSettings> settings)
    : ITextEmbeddingGenerator
{
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct
    )
    {
        using var response = await client.PostAsJsonAsync(
            "api/embed",
            new { model = settings.Value.EmbeddingModel, input = texts },
            ct
        );
        if (!response.IsSuccessStatusCode)
            throw new DocumentIngestionException(
                "embedding-unavailable",
                $"Ollama embedding request failed with HTTP {(int)response.StatusCode}."
            );
        var payload =
            await response.Content.ReadFromJsonAsync<EmbeddingResponse>(ct)
            ?? throw new DocumentIngestionException(
                "invalid-embedding-response",
                "Ollama returned an empty embedding response."
            );
        if (
            payload.Embeddings is null
            || payload.Embeddings.Any(x => x.Length != settings.Value.VectorSize)
        )
            throw new DocumentIngestionException(
                "invalid-embedding-response",
                "Ollama returned embeddings with an unexpected dimension."
            );
        return payload.Embeddings.Select(x => (ReadOnlyMemory<float>)x).ToArray();
    }

    private sealed record EmbeddingResponse(float[][]? Embeddings);
}
