using System.Text.Json;
using Microsoft.Extensions.AI;
using ProductHub.Application.Agents;

namespace ProductHub.Infrastructure.Ai;

internal sealed class OllamaAgentPlanner(IChatClient client) : IAgentPlanner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AgentPlan> CreatePlanAsync(AgentPlanningContext context, CancellationToken ct)
    {
        var system = """
You plan bounded product-data workflows. Return JSON only: {"steps":[{"tool":"tool.name","arguments":{"key":"value"}}]}.
Allowed tools: product.lookup, product.validate, product.propose_update.
Use lookup for facts, validate for data-quality checks, and propose_update only when the objective explicitly requests a product change.
Never invent product facts. A proposal may contain name, brand, or categoryId. Maximum five steps.
""";
        var response = await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, system),
                new ChatMessage(ChatRole.User, JsonSerializer.Serialize(context, Json)),
            ],
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
            ct
        );
        try
        {
            var model = JsonSerializer.Deserialize<ModelPlan>(ExtractJson(response.Text), Json);
            return new AgentPlan(
                model
                    ?.Steps?.Select(x => new AgentToolCall(
                        x.Tool ?? "",
                        x.Arguments?.ToDictionary(
                            k => k.Key,
                            v =>
                                v.Value.ValueKind == JsonValueKind.String
                                    ? v.Value.GetString() ?? ""
                                    : v.Value.GetRawText()
                        )
                    ))
                    .ToArray()
                    ?? []
            );
        }
        catch (JsonException ex)
        {
            throw new AgentWorkflowException(
                "invalid-agent-plan",
                "Model returned an invalid tool plan.",
                ex
            );
        }
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new JsonException("No JSON object was found.");
        return text[start..(end + 1)];
    }

    private sealed record ModelPlan(ModelStep[]? Steps);

    private sealed record ModelStep(string? Tool, Dictionary<string, JsonElement>? Arguments);
}
