namespace ProductHub.Domain.Catalogs;

public sealed class Catalog
{
    private Catalog() { }

    private Catalog(Guid id, string name, string description, DateTimeOffset createdAt)
    {
        Id = id;
        Name = Normalize(name, nameof(name), 120, true);
        Description = Normalize(description, nameof(description), 500, false);
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Catalog Create(
        string name,
        string? description = null,
        DateTimeOffset? now = null
    ) => new(Guid.NewGuid(), name, description ?? string.Empty, now ?? DateTimeOffset.UtcNow);

    private static string Normalize(string value, string parameter, int maxLength, bool required)
    {
        value = value?.Trim() ?? string.Empty;
        if (required && value.Length == 0)
            throw new ArgumentException("Value is required.", parameter);
        if (value.Length > maxLength)
            throw new ArgumentOutOfRangeException(parameter, $"Maximum length is {maxLength}.");
        return value;
    }
}
