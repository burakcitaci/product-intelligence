using ProductHub.Application.Abstractions;
using ProductHub.Domain.Products;

namespace ProductHub.Application.Products;

public sealed class ProductService(
    IProductRepository repository,
    ICatalogRepository? catalogs = null
)
{
    public async Task<ProductDto> CreateAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken
    )
    {
        var errors = Validate(command);
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var sku = command.Sku.Trim().ToUpperInvariant();
        if (await repository.SkuExistsAsync(sku, cancellationToken))
            throw new ConflictException($"A product with SKU '{sku}' already exists.");
        if (
            command.CatalogId is { } catalogId
            && catalogs is not null
            && !await catalogs.ExistsAsync(catalogId, cancellationToken)
        )
            throw new ValidationException(
                new Dictionary<string, string[]> { ["catalogId"] = ["Catalog does not exist."] }
            );

        var attributes = command.Attributes.Select(x => new ProductAttribute(
            x.Name,
            x.Value,
            x.Unit
        ));
        var product = Product.Create(
            sku,
            command.Name,
            command.Brand,
            command.CatalogId,
            command.CategoryId,
            attributes
        );
        await repository.AddAsync(product, cancellationToken);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateSkuException)
        {
            throw new ConflictException($"A product with SKU '{sku}' already exists.");
        }
        return Map(product);
    }

    public async Task<ProductDto?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (await repository.GetAsync(id, cancellationToken)) is { } product ? Map(product) : null;

    public async Task<ProductDto> UpdateAsync(
        UpdateProductCommand command,
        CancellationToken cancellationToken
    )
    {
        var errors = Validate(
            new CreateProductCommand(
                "existing-sku",
                command.Name,
                command.Brand,
                null,
                command.CategoryId,
                command.Attributes
            )
        );
        byte[] rowVersion = [];
        try
        {
            rowVersion = Convert.FromBase64String(command.RowVersion ?? "");
        }
        catch (FormatException)
        {
            errors["rowVersion"] = ["Row version is invalid."];
        }
        if (rowVersion.Length == 0)
            errors["rowVersion"] = ["Row version is required."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var product =
            await repository.GetForUpdateAsync(command.Id, cancellationToken)
            ?? throw new ProductNotFoundException($"Product '{command.Id}' was not found.");
        product.UpdateDetails(
            command.Name,
            command.Brand,
            command.CategoryId,
            command.Attributes.Select(x => new ProductAttribute(x.Name, x.Value, x.Unit))
        );
        repository.SetOriginalRowVersion(product, rowVersion);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (ProductConcurrencyException)
        {
            throw new ConflictException(
                "This product changed while you were editing it. Reload and try again."
            );
        }
        return Map(product);
    }

    public async Task<PagedResult<ProductDto>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken,
        Guid? catalogId = null
    )
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw new ValidationException(
                new Dictionary<string, string[]>
                {
                    ["pagination"] =
                    [
                        "Page must be at least 1 and pageSize must be between 1 and 100.",
                    ],
                }
            );
        var (items, total) = await repository.ListAsync(
            page,
            pageSize,
            catalogId,
            cancellationToken
        );
        return new PagedResult<ProductDto>(items.Select(Map).ToArray(), page, pageSize, total);
    }

    private static Dictionary<string, string[]> Validate(CreateProductCommand command)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(command.Sku) || command.Sku.Trim().Length > 64)
            errors["sku"] = ["SKU is required and must not exceed 64 characters."];
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 200)
            errors["name"] = ["Name is required and must not exceed 200 characters."];
        if (string.IsNullOrWhiteSpace(command.Brand) || command.Brand.Trim().Length > 100)
            errors["brand"] = ["Brand is required and must not exceed 100 characters."];
        if (command.Attributes is null)
            errors["attributes"] = ["Attributes must be supplied."];
        else if (
            command
                .Attributes.GroupBy(x => x.Name?.Trim(), StringComparer.OrdinalIgnoreCase)
                .Any(x => x.Count() > 1)
        )
            errors["attributes"] = ["Attribute names must be unique."];
        return errors;
    }

    private static ProductDto Map(Product product) =>
        new(
            product.Id,
            product.Sku,
            product.Name,
            product.Brand,
            product.CatalogId,
            product.CategoryId,
            product
                .Attributes.Select(x => new ProductAttributeData(x.Name, x.Value, x.Unit))
                .ToArray(),
            product.CreatedAt,
            product.UpdatedAt,
            Convert.ToBase64String(product.RowVersion)
        );
}

public sealed class DuplicateSkuException(Exception innerException)
    : Exception("Duplicate SKU.", innerException);

public sealed class ProductConcurrencyException(Exception innerException)
    : Exception("The product was changed by another user.", innerException);

public sealed class ProductNotFoundException(string message) : Exception(message);
