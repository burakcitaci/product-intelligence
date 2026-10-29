using ProductHub.Domain.Agents;

namespace ProductHub.Application.Agents;

public sealed record StartAgentWorkflowCommand(Guid ProductId, string Objective);

public sealed record AgentToolCall(
    string Tool,
    IReadOnlyDictionary<string, string>? Arguments = null
);

public sealed record AgentPlan(IReadOnlyList<AgentToolCall> Steps);

public sealed record AgentPlanningContext(
    Guid ProductId,
    string Sku,
    string Name,
    string Brand,
    IReadOnlyDictionary<string, string> Attributes,
    string Objective
);

public sealed record ProductUpdateProposal(string? Name, string? Brand, Guid? CategoryId);

public sealed record AgentWorkflowDto(
    Guid Id,
    Guid ProductId,
    string Objective,
    AgentWorkflowStatus Status,
    IReadOnlyList<AgentToolCall> Plan,
    IReadOnlyList<AgentToolResult> Results,
    ProductUpdateProposal? Proposal,
    string? ErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record AgentToolResult(string Tool, bool Succeeded, string Output);

public interface IAgentPlanner
{
    Task<AgentPlan> CreatePlanAsync(
        AgentPlanningContext context,
        CancellationToken cancellationToken
    );
}

public sealed class AgentSettings
{
    public const string SectionName = "Agent";
    public int MaxSteps { get; init; } = 5;
    public int TimeoutSeconds { get; init; } = 120;
    public int MaxArgumentLength { get; init; } = 500;
}

public sealed class AgentWorkflowException(
    string errorCode,
    string message,
    Exception? inner = null
) : Exception(message, inner)
{
    public string ErrorCode { get; } = errorCode;
}
