namespace ProductHub.Application.Content;

public sealed class AiSettings
{
    public const string SectionName = "Ai";
    public string Provider { get; init; } = "Ollama";
    public string Model { get; init; } = "qwen3:8b";
    public string[] Models { get; init; } = ["qwen3:8b", "llama3.1:8b"];
    public Uri Endpoint { get; init; } = new("http://localhost:11434");
    public float Temperature { get; init; } = 0;
    public long? Seed { get; init; } = 42;
    public int MaxOutputTokens { get; init; } = 2048;
    public int TimeoutSeconds { get; init; } = 120;
    public string PromptVersion { get; init; } = "product-description-v4";
}
