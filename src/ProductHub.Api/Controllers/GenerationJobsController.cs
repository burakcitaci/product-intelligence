using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductHub.Application.Content;

namespace ProductHub.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
public sealed class GenerationJobsController(IGenerationJobService jobs) : ControllerBase
{
    [HttpPost("api/products/{productId:guid}/generation-jobs")]
    public async Task<ActionResult<GenerationJobDto>> Enqueue(
        Guid productId,
        GenerateDraftRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? key,
        CancellationToken ct
    )
    {
        var job = await jobs.EnqueueAsync(
            new(
                productId,
                request.Language ?? "de",
                key ?? "",
                HttpContext.TraceIdentifier,
                request.UseRag,
                request.TopK,
                request.Model,
                request.IncludeFacts,
                request.RetrievalMode
            ),
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value,
            ct
        );
        return AcceptedAtAction(nameof(Get), new { id = job.Id }, job);
    }

    [HttpGet("api/generation-jobs/{id:guid}")]
    public Task<GenerationJobDto> Get(Guid id, CancellationToken ct) => jobs.GetAsync(id, ct);

    [HttpGet("api/products/{productId:guid}/generation-jobs")]
    public Task<IReadOnlyList<GenerationJobDto>> List(Guid productId, CancellationToken ct) =>
        jobs.ListAsync(productId, ct);

    [HttpPost("api/generation-jobs/{id:guid}/cancel")]
    public Task<GenerationJobDto> Cancel(Guid id, CancellationToken ct) => jobs.CancelAsync(id, ct);
}
