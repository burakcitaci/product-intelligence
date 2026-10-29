using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductHub.Domain.Catalogs;
using ProductHub.Domain.Products;
using ProductHub.Domain.Users;
using ProductHub.Infrastructure.Security;

namespace ProductHub.Infrastructure.Persistence;

public static class SampleDataSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductHubDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        if (!await db.AppUsers.AnyAsync(cancellationToken))
        {
            db.AppUsers.AddRange(
                AppUser.Create(
                    "admin@producthub.local",
                    "Product Admin",
                    PasswordService.Hash("Admin123!"),
                    AppUserRole.Admin
                ),
                AppUser.Create(
                    "viewer@producthub.local",
                    "Catalog Viewer",
                    PasswordService.Hash("Viewer123!"),
                    AppUserRole.User
                )
            );
        }

        var samples = new[]
        {
            new SampleCatalog(
                "Outdoor Essentials",
                "Reliable gear for hiking, travel, and everyday adventures",
                [
                    new(
                        "OUT-BTL-750",
                        "Alpine Steel Bottle",
                        "Northwind",
                        new("Volume", "750", "ml"),
                        new("Material", "Stainless steel", null),
                        new("Color", "Forest green", null)
                    ),
                    new(
                        "OUT-LAMP-01",
                        "TrailGlow Camping Lantern",
                        "Summit Works",
                        new("Brightness", "600", "lm"),
                        new("Runtime", "18", "hours"),
                        new("Water resistance", "IPX6", null)
                    ),
                    new(
                        "OUT-PACK-28",
                        "RidgeLine Daypack",
                        "Terra Peak",
                        new("Capacity", "28", "L"),
                        new("Weight", "780", "g"),
                        new("Material", "Recycled nylon", null)
                    ),
                ]
            ),
            new SampleCatalog(
                "Home Coffee",
                "Tools for brewing and serving better coffee at home",
                [
                    new(
                        "COF-GRN-01",
                        "QuietBurr Coffee Grinder",
                        "Morning Craft",
                        new("Grind settings", "40", null),
                        new("Hopper capacity", "250", "g"),
                        new("Power", "150", "W")
                    ),
                    new(
                        "COF-PRS-08",
                        "OakPress French Press",
                        "Brew & Co.",
                        new("Capacity", "800", "ml"),
                        new("Material", "Borosilicate glass", null),
                        new("Handle", "Oak wood", null)
                    ),
                    new(
                        "COF-SCL-01",
                        "BrewBalance Scale",
                        "Morning Craft",
                        new("Maximum weight", "3000", "g"),
                        new("Precision", "0.1", "g"),
                        new("Timer", "Integrated", null)
                    ),
                ]
            ),
            new SampleCatalog(
                "Smart Workspace",
                "Practical technology and accessories for a focused desk",
                [
                    new(
                        "DSK-LMP-02",
                        "FocusBeam Desk Lamp",
                        "Luma Office",
                        new("Color temperature", "2700–6500", "K"),
                        new("Power", "12", "W"),
                        new("Control", "Touch dimmer", null)
                    ),
                    new(
                        "DSK-HUB-07",
                        "ConnectSeven USB-C Hub",
                        "Portsmith",
                        new("Ports", "7", null),
                        new("Video output", "4K HDMI", null),
                        new("Power delivery", "100", "W")
                    ),
                    new(
                        "DSK-STD-01",
                        "LiftFrame Laptop Stand",
                        "Form & Function",
                        new("Material", "Aluminium", null),
                        new("Height adjustment", "6 levels", null),
                        new("Maximum load", "8", "kg")
                    ),
                ]
            ),
        };

        foreach (var sample in samples)
        {
            var catalog = await db.Catalogs.SingleOrDefaultAsync(
                x => x.Name == sample.Name,
                cancellationToken
            );
            if (catalog is null)
            {
                catalog = Catalog.Create(sample.Name, sample.Description);
                db.Catalogs.Add(catalog);
            }
            foreach (var item in sample.Products)
            {
                if (await db.Products.AnyAsync(x => x.Sku == item.Sku, cancellationToken))
                    continue;
                db.Products.Add(
                    Product.Create(
                        item.Sku,
                        item.Name,
                        item.Brand,
                        catalog.Id,
                        null,
                        item.Attributes.Select(x => new ProductAttribute(x.Name, x.Value, x.Unit))
                    )
                );
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed record SampleCatalog(string Name, string Description, SampleProduct[] Products);

    private sealed record SampleProduct(
        string Sku,
        string Name,
        string Brand,
        params SampleAttribute[] Attributes
    );

    private sealed record SampleAttribute(string Name, string Value, string? Unit);
}
