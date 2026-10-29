using System.Text.Json;
using Microsoft.Extensions.Options;
using ProductHub.Application.Abstractions;
using ProductHub.Application.Content;
using ProductHub.Application.Products;
using ProductHub.Domain.Agents;
using ProductHub.Domain.Products;

namespace ProductHub.Application.Agents;

public sealed class AgentWorkflowService(
    IAgentWorkflowStore store,
    IAgentPlanner planner,
    IOptions<AgentSettings> settings
)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedTools = new(StringComparer.Ordinal)
    {
        "product.lookup",
        "product.validate",
        "product.propose_update",
    };

    public async Task<AgentWorkflowDto> StartAsync(
        StartAgentWorkflowCommand command,
        CancellationToken cancellationToken
    )
    {
        Validate(command);
        var product =
            await store.GetProductAsync(command.ProductId, false, cancellationToken)
            ?? throw new NotFoundException($"Product '{command.ProductId}' was not found.");
        var workflow = AgentWorkflow.Start(product.Id, command.Objective);
        await store.AddAsync(workflow, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.Value.TimeoutSeconds));
        try
        {
            var context = new AgentPlanningContext(
                product.Id,
                product.Sku,
                product.Name,
                product.Brand,
                product.Attributes.ToDictionary(
                    x => x.Name,
                    x => x.Unit is null ? x.Value : $"{x.Value} {x.Unit}"
                ),
                command.Objective.Trim()
            );
            var plan = await planner.CreatePlanAsync(context, timeout.Token);
            ValidatePlan(plan);
            var results = new List<AgentToolResult>();
            ProductUpdateProposal? proposal = null;
            foreach (var step in plan.Steps)
            {
                switch (step.Tool)
                {
                    case "product.lookup":
                        results.Add(new(step.Tool, true, JsonSerializer.Serialize(context, Json)));
                        break;
                    case "product.validate":
                        results.Add(
                            new(
                                step.Tool,
                                true,
                                JsonSerializer.Serialize(ValidateProduct(product), Json)
                            )
                        );
                        break;
                    case "product.propose_update":
                        proposal = ParseProposal(step.Arguments);
                        results.Add(
                            new(step.Tool, true, "Product update prepared for human approval.")
                        );
                        break;
                }
            }
            var planJson = JsonSerializer.Serialize(plan.Steps, Json);
            var resultsJson = JsonSerializer.Serialize(results, Json);
            if (proposal is null)
                workflow.Complete(planJson, resultsJson);
            else
                workflow.RequestApproval(
                    planJson,
                    resultsJson,
                    JsonSerializer.Serialize(proposal, Json)
                );
            await store.SaveChangesAsync(cancellationToken);
            return Map(workflow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            workflow.Fail("execution-timeout");
            await store.SaveChangesAsync(CancellationToken.None);
            throw new AgentWorkflowException(
                "execution-timeout",
                "Agent workflow exceeded its execution limit."
            );
        }
        catch (AgentWorkflowException ex)
        {
            workflow.Fail(ex.ErrorCode);
            await store.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            workflow.Fail("agent-failed");
            await store.SaveChangesAsync(CancellationToken.None);
            throw new AgentWorkflowException("agent-failed", "Agent workflow failed.", ex);
        }
    }

    public async Task<AgentWorkflowDto> ApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        var workflow =
            await store.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Workflow '{id}' was not found.");
        if (
            workflow.Status != AgentWorkflowStatus.AwaitingApproval
            || workflow.ProposalJson is null
        )
            throw new ConflictException("Workflow is not awaiting approval.");
        var proposal = JsonSerializer.Deserialize<ProductUpdateProposal>(
            workflow.ProposalJson,
            Json
        )!;
        var product =
            await store.GetProductAsync(workflow.ProductId, true, cancellationToken)
            ?? throw new NotFoundException($"Product '{workflow.ProductId}' was not found.");
        product.UpdateDetails(
            proposal.Name ?? product.Name,
            proposal.Brand ?? product.Brand,
            proposal.CategoryId ?? product.CategoryId,
            product.Attributes
        );
        workflow.Approve();
        await store.SaveChangesAsync(cancellationToken);
        return Map(workflow);
    }

    public async Task<AgentWorkflowDto> RejectAsync(Guid id, CancellationToken cancellationToken)
    {
        var workflow =
            await store.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Workflow '{id}' was not found.");
        try
        {
            workflow.Reject();
        }
        catch (InvalidOperationException)
        {
            throw new ConflictException("Workflow is not awaiting approval.");
        }
        await store.SaveChangesAsync(cancellationToken);
        return Map(workflow);
    }

    public async Task<AgentWorkflowDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Map(
            await store.GetAsync(id, cancellationToken)
                ?? throw new NotFoundException($"Workflow '{id}' was not found.")
        );

    private void ValidatePlan(AgentPlan plan)
    {
        if (plan.Steps.Count is 0 || plan.Steps.Count > settings.Value.MaxSteps)
            throw new AgentWorkflowException(
                "step-limit",
                $"Agent plan must contain 1 to {settings.Value.MaxSteps} steps."
            );
        foreach (var step in plan.Steps)
        {
            if (!AllowedTools.Contains(step.Tool))
                throw new AgentWorkflowException(
                    "tool-not-authorized",
                    $"Tool '{step.Tool}' is not authorized."
                );
            if (
                step.Arguments?.Any(x =>
                    x.Key.Length > 100 || x.Value.Length > settings.Value.MaxArgumentLength
                )
                is true
            )
                throw new AgentWorkflowException(
                    "argument-limit",
                    "Tool arguments exceed configured limits."
                );
        }
    }

    private static ProductUpdateProposal ParseProposal(IReadOnlyDictionary<string, string>? args)
    {
        if (args is null)
            throw new AgentWorkflowException(
                "invalid-tool-arguments",
                "Update proposal requires arguments."
            );
        args.TryGetValue("name", out var name);
        args.TryGetValue("brand", out var brand);
        Guid? category =
            args.TryGetValue("categoryId", out var value) && Guid.TryParse(value, out var parsed)
                ? parsed
                : null;
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(brand) && category is null)
            throw new AgentWorkflowException(
                "invalid-tool-arguments",
                "Update proposal contains no changes."
            );
        return new(name, brand, category);
    }

    private static string[] ValidateProduct(Product p)
    {
        var issues = new List<string>();
        if (p.Attributes.Count == 0)
            issues.Add("Product has no attributes.");
        if (p.Name.Length < 3)
            issues.Add("Product name is unusually short.");
        if (p.Brand.Length < 2)
            issues.Add("Brand is unusually short.");
        return issues.ToArray();
    }

    private static void Validate(StartAgentWorkflowCommand c)
    {
        var errors = new Dictionary<string, string[]>();
        if (c.ProductId == Guid.Empty)
            errors["productId"] = ["Product id is required."];
        if (string.IsNullOrWhiteSpace(c.Objective) || c.Objective.Length > 1000)
            errors["objective"] = ["Objective is required and must not exceed 1000 characters."];
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static AgentWorkflowDto Map(AgentWorkflow w) =>
        new(
            w.Id,
            w.ProductId,
            w.Objective,
            w.Status,
            JsonSerializer.Deserialize<AgentToolCall[]>(w.PlanJson, Json) ?? [],
            JsonSerializer.Deserialize<AgentToolResult[]>(w.ResultsJson, Json) ?? [],
            w.ProposalJson is null
                ? null
                : JsonSerializer.Deserialize<ProductUpdateProposal>(w.ProposalJson, Json),
            w.ErrorCode,
            w.CreatedAt,
            w.UpdatedAt
        );
}
