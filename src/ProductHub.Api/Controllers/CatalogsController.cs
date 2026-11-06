using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductHub.Application.Catalogs;

namespace ProductHub.Api.Controllers;

[ApiController]
[Route("api/catalogs")]
[Authorize]
public sealed class CatalogsController(CatalogService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CatalogDto>>> List(
        CancellationToken cancellationToken
    ) => Ok(await service.ListAsync(cancellationToken));

    [HttpGet("{id:guid}/review-queue")]
    [Authorize(Roles = "Admin")]
    public Task<ReviewQueueResult> ReviewQueue(
        Guid id,
        CancellationToken ct,
        [FromQuery] string language = "de",
        [FromQuery] string filter = "all",
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25
    ) => service.GetReviewQueueAsync(id, language, filter, search, page, pageSize, ct);

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CatalogDto>> Create(
        CreateCatalogRequest request,
        CancellationToken cancellationToken
    )
    {
        var catalog = await service.CreateAsync(
            new CreateCatalogCommand(request.Name, request.Description),
            cancellationToken
        );
        return Created($"/api/catalogs/{catalog.Id}", catalog);
    }
}

public sealed record CreateCatalogRequest(string Name, string? Description);
