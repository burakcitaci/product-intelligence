namespace ProductHub.Domain.Agents;

public enum AgentWorkflowStatus
{
    Running,
    AwaitingApproval,
    Completed,
    Rejected,
    Failed,
}

public sealed class AgentWorkflow
{
    private AgentWorkflow() { }

    private AgentWorkflow(Guid id, Guid productId, string objective) =>
        (Id, ProductId, Objective, Status, CreatedAt, UpdatedAt) = (
            id,
            productId,
            objective,
            AgentWorkflowStatus.Running,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow
        );

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public string Objective { get; private set; } = "";
    public AgentWorkflowStatus Status { get; private set; }
    public string PlanJson { get; private set; } = "[]";
    public string ResultsJson { get; private set; } = "[]";
    public string? ProposalJson { get; private set; }
    public string? ErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AgentWorkflow Start(Guid productId, string objective)
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("Product id is required.", nameof(productId));
        ArgumentException.ThrowIfNullOrWhiteSpace(objective);
        return new AgentWorkflow(Guid.NewGuid(), productId, objective.Trim());
    }

    public void Complete(string planJson, string resultsJson)
    {
        EnsureRunning();
        PlanJson = planJson;
        ResultsJson = resultsJson;
        Status = AgentWorkflowStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RequestApproval(string planJson, string resultsJson, string proposalJson)
    {
        EnsureRunning();
        PlanJson = planJson;
        ResultsJson = resultsJson;
        ProposalJson = proposalJson;
        Status = AgentWorkflowStatus.AwaitingApproval;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Approve()
    {
        if (Status != AgentWorkflowStatus.AwaitingApproval)
            throw new InvalidOperationException("Workflow is not awaiting approval.");
        Status = AgentWorkflowStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Reject()
    {
        if (Status != AgentWorkflowStatus.AwaitingApproval)
            throw new InvalidOperationException("Workflow is not awaiting approval.");
        Status = AgentWorkflowStatus.Rejected;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string errorCode)
    {
        if (Status is AgentWorkflowStatus.Completed or AgentWorkflowStatus.Rejected)
            return;
        ErrorCode = errorCode;
        Status = AgentWorkflowStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureRunning()
    {
        if (Status != AgentWorkflowStatus.Running)
            throw new InvalidOperationException("Workflow is not running.");
    }
}
