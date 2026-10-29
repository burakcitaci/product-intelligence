namespace ProductHub.Domain.Products;

public sealed record ProductAttribute
{
    private ProductAttribute() { }

    public ProductAttribute(string name, string value, string? unit = null)
    {
        Name = Required(name, nameof(name), 100);
        Value = Required(value, nameof(value), 500);
        Unit = Optional(unit, 30);
    }

    public string Name { get; private init; } = null!;
    public string Value { get; private init; } = null!;
    public string? Unit { get; private init; }

    private static string Required(string value, string parameter, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        value = value.Trim();
        if (value.Length > maxLength)
            throw new ArgumentOutOfRangeException(parameter, $"Maximum length is {maxLength}.");
        return value;
    }

    private static string? Optional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        if (value.Length > maxLength)
            throw new ArgumentOutOfRangeException(nameof(value), $"Maximum length is {maxLength}.");
        return value;
    }
}
