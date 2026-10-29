using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductHub.Application.Agents;

namespace ProductHub.Api.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/agent/workflows")]
public sealed class AgentWorkflowsController(AgentWorkflowService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<AgentWorkflowDto>> Start(
        StartAgentWorkflowRequest request,
        CancellationToken ct
    )
    {
        var workflow = await service.StartAsync(new(request.ProductId, request.Objective), ct);
        return CreatedAtAction(nameof(Get), new { id = workflow.Id }, workflow);
    }

    [HttpGet("{id:guid}")]
    public Task<AgentWorkflowDto> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);

    [HttpPost("{id:guid}/approve")]
    public Task<AgentWorkflowDto> Approve(Guid id, CancellationToken ct) =>
        service.ApproveAsync(id, ct);

    [HttpPost("{id:guid}/reject")]
    public Task<AgentWorkflowDto> Reject(Guid id, CancellationToken ct) =>
        service.RejectAsync(id, ct);
}

public sealed record StartAgentWorkflowRequest(Guid ProductId, string Objective);
