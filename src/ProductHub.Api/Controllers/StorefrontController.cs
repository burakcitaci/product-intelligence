using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductHub.Domain.Content;
using ProductHub.Domain.Products;
using ProductHub.Infrastructure.Persistence;

namespace ProductHub.Api.Controllers;

[ApiController, Authorize, Route("api/storefront")]
public sealed class StorefrontController(ProductHubDbContext db) : ControllerBase
{
    [HttpGet("catalogs/{catalogId:guid}/products")]
    public async Task<ActionResult<IReadOnlyList<StorefrontProductResponse>>> List(
        Guid catalogId,
        [FromQuery] string language = "de",
        CancellationToken cancellationToken = default
    )
    {
        language = language.Trim().ToLowerInvariant();
        if (language is not ("de" or "en"))
            return BadRequest(
                new ProblemDetails
                {
                    Title = "Unsupported language",
                    Detail = "Language must be 'de' or 'en'.",
                }
            );
        var products = await db
            .Products.AsNoTracking()
            .Where(x => x.CatalogId == catalogId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var ids = products.Select(x => x.Id).ToArray();
        var current = await db
            .ContentDrafts.AsNoTracking()
            .Where(x =>
                ids.Contains(x.ProductId)
                && x.Status == ContentDraftStatus.Approved
                && x.IsCurrent
                && x.Language == language
            )
            .ToDictionaryAsync(x => x.ProductId, cancellationToken);
        return Ok(products.Select(x => Map(x, current.GetValueOrDefault(x.Id))).ToArray());
    }

    private static StorefrontProductResponse Map(Product product, ContentDraft? draft) =>
        new(
            product.Id,
            product.Sku,
            product.Name,
            product.Brand,
            product.Attributes,
            draft is null
                ? null
                : new(
                    draft.Title,
                    draft.ShortDescription,
                    draft.LongDescription,
                    draft.Highlights,
                    draft.Language
                )
        );
}

public sealed record StorefrontProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string Brand,
    IReadOnlyList<ProductAttribute> Attributes,
    ApprovedContentResponse? ApprovedContent
);

public sealed record ApprovedContentResponse(
    string Title,
    string ShortDescription,
    string LongDescription,
    IReadOnlyList<string> Highlights,
    string Language
);
