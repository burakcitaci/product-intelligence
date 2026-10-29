namespace ProductHub.Domain.Products;

public sealed class Product
{
    private Product() { }

    private Product(
        Guid id,
        string sku,
        string name,
        string brand,
        Guid? catalogId,
        Guid? categoryId,
        IReadOnlyCollection<ProductAttribute> attributes,
        DateTimeOffset createdAt
    )
    {
        Id = id;
        Sku = NormalizeRequired(sku, nameof(sku), 64).ToUpperInvariant();
        Name = NormalizeRequired(name, nameof(name), 200);
        Brand = NormalizeRequired(brand, nameof(brand), 100);
        CatalogId = catalogId;
        CategoryId = categoryId;
        Attributes = attributes.ToList();
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string Sku { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Brand { get; private set; } = null!;
    public Guid? CatalogId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public List<ProductAttribute> Attributes { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Product Create(
        string sku,
        string name,
        string brand,
        Guid? categoryId = null,
        IEnumerable<ProductAttribute>? attributes = null,
        DateTimeOffset? now = null
    ) => Create(sku, name, brand, null, categoryId, attributes, now);

    public static Product Create(
        string sku,
        string name,
        string brand,
        Guid? catalogId,
        Guid? categoryId,
        IEnumerable<ProductAttribute>? attributes = null,
        DateTimeOffset? now = null
    )
    {
        var materialized = (attributes ?? []).ToArray();
        if (
            materialized.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != materialized.Length
        )
            throw new ArgumentException("Attribute names must be unique.", nameof(attributes));

        return new Product(
            Guid.NewGuid(),
            sku,
            name,
            brand,
            catalogId,
            categoryId,
            materialized,
            now ?? DateTimeOffset.UtcNow
        );
    }

    public void UpdateDetails(
        string name,
        string brand,
        Guid? categoryId,
        IEnumerable<ProductAttribute> attributes,
        DateTimeOffset? now = null
    )
    {
        var materialized = attributes.ToArray();
        if (
            materialized.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != materialized.Length
        )
            throw new ArgumentException("Attribute names must be unique.", nameof(attributes));
        Name = NormalizeRequired(name, nameof(name), 200);
        Brand = NormalizeRequired(brand, nameof(brand), 100);
        CategoryId = categoryId;
        Attributes = materialized.ToList();
        UpdatedAt = now ?? DateTimeOffset.UtcNow;
    }

    private static string NormalizeRequired(string value, string parameter, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        value = value.Trim();
        if (value.Length > maxLength)
            throw new ArgumentOutOfRangeException(parameter, $"Maximum length is {maxLength}.");
        return value;
    }
}
