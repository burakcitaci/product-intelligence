namespace ProductHub.Application.Catalogs;

public sealed record CreateCatalogCommand(string Name, string? Description);

public sealed record CatalogDto(
    Guid Id,
    string Name,
    string Description,
    int ProductCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
