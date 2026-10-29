using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using ProductHub.Application.Knowledge;

namespace ProductHub.Infrastructure.Knowledge;

internal sealed class QdrantVectorStore(HttpClient client, IOptions<RagSettings> settings)
    : IVectorStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _ready;

    public async Task UpsertAsync(IReadOnlyList<VectorDocument> documents, CancellationToken ct)
    {
        if (documents.Count == 0)
            return;
        await EnsureCollectionAsync(ct);
        var points = documents.Select(x => new
        {
            id = x.Id,
            vector = x.Vector.ToArray(),
            payload = new { documentId = x.DocumentId, ordinal = x.Ordinal },
        });
        using var response = await client.PutAsJsonAsync(
            $"collections/{settings.Value.CollectionName}/points?wait=true",
            new { points },
            ct
        );
        await EnsureSuccessAsync(response, "qdrant-upsert-failed", ct);
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        ReadOnlyMemory<float> vector,
        int limit,
        CancellationToken ct
    )
    {
        await EnsureCollectionAsync(ct);
        using var response = await client.PostAsJsonAsync(
            $"collections/{settings.Value.CollectionName}/points/query",
            new
            {
                query = vector.ToArray(),
                limit,
                with_payload = false,
            },
            ct
        );
        await EnsureSuccessAsync(response, "qdrant-search-failed", ct);
        var p = await response.Content.ReadFromJsonAsync<QueryResponse>(ct);
        return p?.Result?.Points?.Select(x => new VectorSearchHit(x.Id, x.Score)).ToArray() ?? [];
    }

    private async Task EnsureCollectionAsync(CancellationToken ct)
    {
        if (_ready)
            return;
        await _lock.WaitAsync(ct);
        try
        {
            if (_ready)
                return;
            using var get = await client.GetAsync(
                $"collections/{settings.Value.CollectionName}",
                ct
            );
            if (get.StatusCode == HttpStatusCode.NotFound)
            {
                using var create = await client.PutAsJsonAsync(
                    $"collections/{settings.Value.CollectionName}",
                    new { vectors = new { size = settings.Value.VectorSize, distance = "Cosine" } },
                    ct
                );
                await EnsureSuccessAsync(create, "qdrant-collection-failed", ct);
            }
            else
                await EnsureSuccessAsync(get, "qdrant-unavailable", ct);
            _ready = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string code,
        CancellationToken ct
    )
    {
        if (response.IsSuccessStatusCode)
            return;
        throw new DocumentIngestionException(
            code,
            $"Qdrant request failed with HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}"
        );
    }

    private sealed record QueryResponse(QueryResult? Result);

    private sealed record QueryResult(QueryPoint[]? Points);

    private sealed record QueryPoint(Guid Id, float Score);
}
