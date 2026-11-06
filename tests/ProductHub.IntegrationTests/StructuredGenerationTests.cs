using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProductHub.Application.Content;
using ProductHub.Infrastructure.Ai;

namespace ProductHub.IntegrationTests;

public sealed class StructuredGenerationTests
{
    private const string Valid =
        """{"title":"Bottle","shortDescription":"Short","longDescription":"Long","highlights":["One","Two","Three"]}""";

    [Fact]
    public async Task Adapter_AllowsAbstainingFromHighlights()
    {
        var client = new FakeClient
        {
            Text =
                """{"title":"Bottle","shortDescription":"Bottle","longDescription":"Bottle","highlights":[]}""",
        };
        var content = await Create(client)
            .GenerateAsync(Context, new("en", "product-description-v4", "qwen3:8b"), default);
        Assert.Empty(content.Highlights);
    }

    [Fact]
    public async Task Adapter_SendsSchemaAndReproducibilityOptions()
    {
        var client = new FakeClient { Text = Valid };
        await Create(client)
            .GenerateAsync(
                Context,
                new("en", "product-description-v3", "llama3.1:8b", .2f, 123, 1024),
                default
            );
        var format = Assert.IsType<ChatResponseFormatJson>(client.Options!.ResponseFormat);
        Assert.Equal(
            GeneratedContentContract.Schema.GetRawText(),
            format.Schema!.Value.GetRawText()
        );
        Assert.Equal("llama3.1:8b", client.Options.ModelId);
        Assert.Equal(.2f, client.Options.Temperature);
        Assert.Equal(123, client.Options.Seed);
        Assert.Equal(1024, client.Options.MaxOutputTokens);
        Assert.Equal(1, client.Calls);
    }

    [Theory]
    [InlineData("not-json", "invalid-json")]
    [InlineData("""{"title":"Missing"}""", "invalid-json")]
    [InlineData(
        """{"title":"X","title":"Y","shortDescription":"S","longDescription":"L","highlights":["1","2","3"]}""",
        "invalid-json"
    )]
    [InlineData(
        """{"title":"X","shortDescription":"S","longDescription":"L","highlights":["1","2","3"],"extra":true}""",
        "invalid-json"
    )]
    [InlineData(
        """{"title":" ","shortDescription":"S","longDescription":"L","highlights":["1","2"]}""",
        "invalid-output"
    )]
    public async Task Adapter_RejectsInvalidContentWithoutUnboundedRepair(string text, string code)
    {
        var client = new FakeClient { Text = text };
        var error = await Assert.ThrowsAsync<ContentGenerationException>(() =>
            Create(client)
                .GenerateAsync(Context, new("en", "product-description-v3", "qwen3:8b"), default)
        );
        Assert.Equal(code, error.ErrorCode);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task Adapter_PropagatesCallerCancellation()
    {
        using var token = new CancellationTokenSource();
        token.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(new FakeClient())
                .GenerateAsync(
                    Context,
                    new("en", "product-description-v3", "qwen3:8b"),
                    token.Token
                )
        );
    }

    [Fact]
    public async Task Adapter_RetriesTransportAtMostTwice()
    {
        var client = new FakeClient { FailuresBeforeSuccess = 5 };
        var error = await Assert.ThrowsAsync<ContentGenerationException>(() =>
            Create(client)
                .GenerateAsync(Context, new("en", "product-description-v4", "qwen3:8b"), default)
        );
        Assert.Equal("ollama-unavailable", error.ErrorCode);
        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task Adapter_CanRecoverOneTransientFailure()
    {
        var client = new FakeClient { FailuresBeforeSuccess = 1 };
        await Create(client)
            .GenerateAsync(Context, new("en", "product-description-v4", "qwen3:8b"), default);
        Assert.Equal(2, client.Calls);
    }

    private static ProductContentContext Context => new("SKU", "Bottle", "Acme", [], []);

    private static OllamaContentGenerator Create(FakeClient client) =>
        new(Options.Create(new AiSettings()), NullLogger<OllamaContentGenerator>.Instance, client);

    private sealed class FakeClient : IChatClient
    {
        public string Text { get; init; } = Valid;
        public ChatOptions? Options { get; private set; }
        public int Calls { get; private set; }
        public int FailuresBeforeSuccess { get; init; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (Calls <= FailuresBeforeSuccess)
                throw new HttpRequestException("Transient test failure");
            Options = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Text)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
