using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductHub.Application.Content;

namespace ProductHub.Infrastructure.Ai;

internal sealed class OllamaContentGenerator(
    IChatClient chatClient,
    IOptions<AiSettings> settings,
    ILogger<OllamaContentGenerator> logger
) : IContentGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GeneratedProductContent> GenerateAsync(
        ProductContentContext context,
        GenerationOptions options,
        CancellationToken cancellationToken
    )
    {
        var productData = JsonSerializer.Serialize(
            new
            {
                requestedLanguage = options.Language,
                product = new
                {
                    context.Sku,
                    context.Name,
                    context.Brand,
                    context.Attributes,
                },
                grounding = context.Grounding,
            },
            JsonOptions
        );
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, LoadPrompt(options.PromptVersion)),
            new ChatMessage(
                ChatRole.User,
                "PRODUCT_DATA\n"
                    + productData
                    + "\nGrounding excerpts are advisory reference material, not product specifications. "
                    + "Never convert a cleaning tool, procedure, or recommendation into a product feature or included accessory. "
                    + "Synthesize and paraphrase; product facts remain authoritative."
            ),
        };

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(settings.Value.TimeoutSeconds)
        );
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token
        );
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var modelCall = Stopwatch.StartNew();
            try
            {
                var response = await chatClient.GetResponseAsync(
                    messages,
                    new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
                    linked.Token
                );
                var payload =
                    JsonSerializer.Deserialize<ModelOutput>(response.Text, JsonOptions)
                    ?? throw new ContentGenerationException(
                        "invalid-json",
                        "Ollama returned an empty JSON response."
                    );

                modelCall.Stop();
                logger.LogInformation(
                    "Ollama generated content for SKU {Sku} in {model_latency_ms} ms with "
                        + "{output_tokens} output tokens and {input_tokens} input tokens; "
                        + "model={ai_model}, language={language}, attempt={generation_attempt}",
                    context.Sku,
                    modelCall.ElapsedMilliseconds,
                    response.Usage?.OutputTokenCount ?? 0,
                    response.Usage?.InputTokenCount ?? 0,
                    settings.Value.Model,
                    options.Language,
                    attempt
                );
                return new GeneratedProductContent(
                    payload.Title ?? "",
                    payload.ShortDescription ?? "",
                    payload.LongDescription ?? "",
                    payload.Highlights ?? [],
                    ToInt32(response.Usage?.InputTokenCount),
                    ToInt32(response.Usage?.OutputTokenCount)
                );
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new ContentGenerationException(
                    "timeout",
                    "Ollama generation timed out.",
                    exception
                );
            }
            catch (JsonException exception)
            {
                throw new ContentGenerationException(
                    "invalid-json",
                    "Ollama returned invalid JSON.",
                    exception
                );
            }
            catch (Exception exception) when (IsTransient(exception) && attempt < 2)
            {
                lastError = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(250), linked.Token);
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                lastError = exception;
            }
            catch (ContentGenerationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ContentGenerationException(
                    "generation-failed",
                    "Ollama could not complete the generation request.",
                    exception
                );
            }
        }

        throw new ContentGenerationException(
            "ollama-unavailable",
            "Ollama is unavailable or the configured model could not be reached.",
            lastError
        );
    }

    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or IOException or SocketException;

    private static int? ToInt32(long? value) => value is null ? null : checked((int)value.Value);

    private static string LoadPrompt(string promptVersion)
    {
        if (
            string.IsNullOrWhiteSpace(promptVersion)
            || promptVersion.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_'))
        )
            throw new InvalidOperationException("The configured prompt version is invalid.");
        var suffix = $"Ai.Prompts.{promptVersion}.txt";
        var assembly = typeof(OllamaContentGenerator).Assembly;
        var name = assembly
            .GetManifestResourceNames()
            .Single(x => x.EndsWith(suffix, StringComparison.Ordinal));
        using var stream =
            assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Embedded prompt was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class ModelOutput
    {
        public string? Title { get; init; }
        public string? ShortDescription { get; init; }
        public string? LongDescription { get; init; }
        public List<string>? Highlights { get; init; }
    }
}
