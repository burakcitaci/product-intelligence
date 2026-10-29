namespace ProductHub.Application.Products;

public sealed record ProductAttributeData(string Name, string Value, string? Unit);

public sealed record CreateProductCommand(
    string Sku,
    string Name,
    string Brand,
    Guid? CatalogId,
    Guid? CategoryId,
    IReadOnlyList<ProductAttributeData> Attributes
);

public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    string Brand,
    Guid? CatalogId,
    Guid? CategoryId,
    IReadOnlyList<ProductAttributeData> Attributes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string RowVersion
);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class ConflictException(string message) : Exception(message);
