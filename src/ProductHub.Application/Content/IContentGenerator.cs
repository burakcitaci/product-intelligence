namespace ProductHub.Application.Content;

public interface IContentGenerator
{
    Task<GeneratedProductContent> GenerateAsync(
        ProductContentContext context,
        GenerationOptions options,
        CancellationToken cancellationToken
    );
}

public sealed record ProductContentContext(
    string Sku,
    string Name,
    string Brand,
    IReadOnlyList<ProductFact> Attributes
);

public sealed record ProductFact(string Name, string Value, string? Unit);

public sealed record GenerationOptions(string Language, string PromptVersion);

public sealed record GeneratedProductContent(
    string Title,
    string ShortDescription,
    string LongDescription,
    IReadOnlyList<string> Highlights,
    int? InputTokens = null,
    int? OutputTokens = null
);

public sealed class ContentGenerationException(
    string errorCode,
    string message,
    Exception? innerException = null
) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
