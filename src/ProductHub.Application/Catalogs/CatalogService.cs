using ProductHub.Application.Abstractions;
using ProductHub.Application.Content;
using ProductHub.Application.Products;
using ProductHub.Domain.Catalogs;

namespace ProductHub.Application.Catalogs;

public sealed class CatalogService(ICatalogRepository repository)
{
    public async Task<CatalogDto> CreateAsync(
        CreateCatalogCommand command,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 120)
            throw new ValidationException(
                new Dictionary<string, string[]>
                {
                    ["name"] = ["Name is required and must not exceed 120 characters."],
                }
            );
        if ((command.Description?.Trim().Length ?? 0) > 500)
            throw new ValidationException(
                new Dictionary<string, string[]>
                {
                    ["description"] = ["Description must not exceed 500 characters."],
                }
            );
        if (await repository.NameExistsAsync(command.Name.Trim(), cancellationToken))
            throw new ConflictException($"A catalog named '{command.Name.Trim()}' already exists.");
        var catalog = Catalog.Create(command.Name, command.Description);
        await repository.AddAsync(catalog, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Map(catalog, 0);
    }

    public async Task<IReadOnlyList<CatalogDto>> ListAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken))
            .Select(x => Map(x.Catalog, x.ProductCount))
            .ToArray();

    public async Task<ReviewQueueResult> GetReviewQueueAsync(
        Guid catalogId,
        string language,
        string filter,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct
    )
    {
        var errors = new Dictionary<string, string[]>();
        language = language.Trim().ToLowerInvariant();
        filter = filter.Trim().ToLowerInvariant();
        search = search?.Trim() ?? "";
        if (language is not ("de" or "en"))
            errors["language"] = ["Language must be de or en."];
        if (filter is not ("all" or "pending" or "missing-language" or "missing-facts" or "stale"))
            errors["filter"] = ["Choose a supported review queue filter."];
        if (page < 1)
            errors["page"] = ["Page must be positive."];
        if (pageSize is < 1 or > 100)
            errors["pageSize"] = ["Page size must be between 1 and 100."];
        if (search.Length > 200)
            errors["search"] = ["Search must not exceed 200 characters."];
        if (errors.Count > 0)
            throw new ValidationException(errors);
        if (!await repository.ExistsAsync(catalogId, ct))
            throw new NotFoundException("Catalog was not found.");
        return await repository.GetReviewQueueAsync(
            catalogId,
            language,
            filter,
            search,
            page,
            pageSize,
            ct
        );
    }

    private static CatalogDto Map(Catalog catalog, int count) =>
        new(
            catalog.Id,
            catalog.Name,
            catalog.Description,
            count,
            catalog.CreatedAt,
            catalog.UpdatedAt
        );
}
