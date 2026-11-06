using System.Text.Json;

namespace ProductHub.Application.Content;

public static class GeneratedContentContract
{
    public const string Version = "product-content-v2";
    public static JsonElement Schema { get; } =
        JsonSerializer.SerializeToElement(
            new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "title", "shortDescription", "longDescription", "highlights" },
                properties = new
                {
                    title = new
                    {
                        type = "string",
                        minLength = 1,
                        maxLength = 200,
                    },
                    shortDescription = new
                    {
                        type = "string",
                        minLength = 1,
                        maxLength = 500,
                    },
                    longDescription = new
                    {
                        type = "string",
                        minLength = 1,
                        maxLength = 8000,
                    },
                    highlights = new
                    {
                        type = "array",
                        minItems = 0,
                        maxItems = 5,
                        items = new
                        {
                            type = "string",
                            minLength = 1,
                            maxLength = 300,
                        },
                    },
                },
            }
        );

    public static void Validate(GeneratedProductContent content)
    {
        if (
            string.IsNullOrWhiteSpace(content.Title)
            || content.Title.Length > 200
            || string.IsNullOrWhiteSpace(content.ShortDescription)
            || content.ShortDescription.Length > 500
            || string.IsNullOrWhiteSpace(content.LongDescription)
            || content.LongDescription.Length > 8000
            || content.Highlights is null
            || content.Highlights.Count > 5
            || content.Highlights.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 300)
        )
            throw new ContentGenerationException(
                "invalid-output",
                "The model returned incomplete or invalid JSON content."
            );
    }
}
