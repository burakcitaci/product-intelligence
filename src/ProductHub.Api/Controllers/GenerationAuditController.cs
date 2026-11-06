using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductHub.Application.Content;
using ProductHub.Application.Products;

namespace ProductHub.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
public sealed class GenerationAuditController(IGenerationAuditReader audit) : ControllerBase
{
    [HttpGet("api/products/{productId:guid}/generations")]
    public Task<PagedResult<GenerationAuditEntryDto>> List(
        Guid productId,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? status = null,
        [FromQuery] string? language = null
    ) => audit.ReadAsync(productId, page, pageSize, status, language, ct);
}
