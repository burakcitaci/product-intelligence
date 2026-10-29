namespace ProductHub.Application.Knowledge;

public sealed class RagSettings
{
    public const string SectionName = "Rag";
    public Uri OllamaEndpoint { get; init; } = new("http://localhost:11434");
    public string EmbeddingModel { get; init; } = "nomic-embed-text";
    public Uri QdrantEndpoint { get; init; } = new("http://localhost:6333");
    public string CollectionName { get; init; } = "product-knowledge";
    public int VectorSize { get; init; } = 768;
    public int ChunkSize { get; init; } = 1200;
    public int ChunkOverlap { get; init; } = 200;
    public int MaxUploadBytes { get; init; } = 10 * 1024 * 1024;
}
