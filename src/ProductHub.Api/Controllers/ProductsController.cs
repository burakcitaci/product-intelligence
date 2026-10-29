using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductHub.Application.Products;

namespace ProductHub.Api.Controllers;

[ApiController]
[Route("api/products")]
[Authorize]
public sealed class ProductsController(ProductService service) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> Create(
        CreateProductRequest request,
        CancellationToken cancellationToken
    )
    {
        var product = await service.CreateAsync(
            new CreateProductCommand(
                request.Sku,
                request.Name,
                request.Brand,
                request.CatalogId,
                request.CategoryId,
                request
                    .Attributes?.Select(x => new ProductAttributeData(x.Name, x.Value, x.Unit))
                    .ToArray()
                    ?? []
            ),
            cancellationToken
        );
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken) is { } product ? Ok(product) : NotFound();

    [HttpGet]
    [ProducesResponseType<PagedResult<ProductDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProductDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? catalogId = null,
        CancellationToken cancellationToken = default
    ) => Ok(await service.ListAsync(page, pageSize, cancellationToken, catalogId));
}

public sealed record CreateProductRequest(
    string Sku,
    string Name,
    string Brand,
    Guid? CatalogId,
    Guid? CategoryId,
    IReadOnlyList<ProductAttributeRequest>? Attributes
);

public sealed record ProductAttributeRequest(string Name, string Value, string? Unit);
