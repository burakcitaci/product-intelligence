using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductHub.Domain.Products;

namespace ProductHub.Infrastructure.Persistence;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable(
            "Products",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Products_Sku_NotEmpty",
                    "LEN(LTRIM(RTRIM([Sku]))) > 0"
                );
                table.HasCheckConstraint(
                    "CK_Products_Name_NotEmpty",
                    "LEN(LTRIM(RTRIM([Name]))) > 0"
                );
                table.HasCheckConstraint(
                    "CK_Products_Brand_NotEmpty",
                    "LEN(LTRIM(RTRIM([Brand]))) > 0"
                );
            }
        );
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Sku).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.HasIndex(x => x.Sku).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Brand).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.CatalogId);
        builder.HasIndex(x => new { x.Brand, x.Name });
        builder.Property(x => x.CreatedAt).HasPrecision(3);
        builder.Property(x => x.UpdatedAt).HasPrecision(3);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder
            .Property(x => x.Attributes)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                value => JsonSerializer.Serialize(value, JsonOptions),
                value =>
                    JsonSerializer.Deserialize<List<ProductAttribute>>(value, JsonOptions) ?? new()
            )
            .Metadata.SetValueComparer(
                new ValueComparer<List<ProductAttribute>>(
                    (left, right) =>
                        JsonSerializer.Serialize(left, JsonOptions)
                        == JsonSerializer.Serialize(right, JsonOptions),
                    value =>
                        JsonSerializer
                            .Serialize(value, JsonOptions)
                            .GetHashCode(StringComparison.Ordinal),
                    value =>
                        JsonSerializer.Deserialize<List<ProductAttribute>>(
                            JsonSerializer.Serialize(value, JsonOptions),
                            JsonOptions
                        )!
                )
            );
    }
}
