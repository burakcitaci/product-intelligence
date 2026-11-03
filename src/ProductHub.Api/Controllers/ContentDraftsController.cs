using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductHub.Application.Content;

namespace ProductHub.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
public sealed class ContentDraftsController(ContentGenerationService service) : ControllerBase
{
    [HttpPost("api/products/{productId:guid}/drafts/generate")]
    [ProducesResponseType<ContentDraftDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ContentDraftDto>> Generate(
        Guid productId,
        GenerateDraftRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken
    )
    {
        var draft = await service.GenerateAsync(
            new GenerateDraftCommand(
                productId,
                request.Language ?? "de",
                idempotencyKey ?? "",
                HttpContext.TraceIdentifier,
                request.UseRag,
                request.TopK,
                request.Model,
                request.IncludeFacts,
                request.RetrievalMode
            ),
            cancellationToken
        );
        return CreatedAtAction(nameof(Get), new { id = draft.Id }, draft);
    }

    [HttpGet("api/products/{productId:guid}/drafts")]
    [ProducesResponseType<IReadOnlyList<ContentDraftDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ContentDraftDto>>> List(
        Guid productId,
        CancellationToken cancellationToken
    ) => Ok(await service.ListAsync(productId, cancellationToken));

    [HttpGet("api/drafts/{id:guid}")]
    [ProducesResponseType<ContentDraftDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ContentDraftDto>> Get(
        Guid id,
        CancellationToken cancellationToken
    ) => Ok(await service.GetAsync(id, cancellationToken));

    [HttpGet("api/drafts/{id:guid}/generation")]
    public async Task<ActionResult<AiGenerationMetadataDto>> GetGeneration(
        Guid id,
        CancellationToken cancellationToken
    ) => Ok(await service.GetGenerationMetadataAsync(id, cancellationToken));

    [HttpPost("api/drafts/{id:guid}/approve")]
    public Task<ContentDraftDto> Approve(Guid id, CancellationToken cancellationToken) =>
        service.ReviewAsync(id, true, cancellationToken);

    [HttpPost("api/drafts/{id:guid}/reject")]
    public Task<ContentDraftDto> Reject(Guid id, CancellationToken cancellationToken) =>
        service.ReviewAsync(id, false, cancellationToken);

    [HttpPost("api/drafts/{id:guid}/current")]
    public Task<ContentDraftDto> SetCurrent(Guid id, CancellationToken cancellationToken) =>
        service.SetCurrentAsync(id, cancellationToken);

    [HttpPut("api/drafts/{id:guid}")]
    public Task<ContentDraftDto> Update(
        Guid id,
        UpdateDraftRequest request,
        CancellationToken cancellationToken
    ) =>
        service.UpdateDraftAsync(
            id,
            new UpdateDraftCommand(
                request.Title,
                request.ShortDescription,
                request.LongDescription,
                request.Highlights ?? []
            ),
            cancellationToken
        );
}

/// <summary>Generation settings. RetrievalMode accepts None, Naive, or Advanced; when omitted, UseRag selects Naive or None. IncludeFacts controls structured attributes.</summary>
public sealed record GenerateDraftRequest(
    string? Language = "de",
    bool UseRag = false,
    int TopK = 5,
    string? Model = null,
    bool IncludeFacts = true,
    string? RetrievalMode = null
);

public sealed record UpdateDraftRequest(
    string Title,
    string ShortDescription,
    string LongDescription,
    IReadOnlyList<string>? Highlights
);
