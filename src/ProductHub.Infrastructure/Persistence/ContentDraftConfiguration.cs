using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductHub.Domain.Content;
using ProductHub.Domain.Products;

namespace ProductHub.Infrastructure.Persistence;

internal sealed class ContentDraftConfiguration : IEntityTypeConfiguration<ContentDraft>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<ContentDraft> builder)
    {
        builder.ToTable("ContentDrafts");
        builder.HasKey(x => x.Id);
        builder
            .HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasOne<AiGeneration>()
            .WithOne()
            .HasForeignKey<ContentDraft>(x => x.GenerationId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.Property(x => x.Language).HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(x => x.ClaimReviewJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ShortDescription).HasMaxLength(500).IsRequired();
        builder.Property(x => x.LongDescription).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
        builder.Property(x => x.CreatedAt).HasPrecision(3);
        builder.Property(x => x.IsCurrent).HasDefaultValue(false);
        builder
            .Property(x => x.Highlights)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                value => JsonSerializer.Serialize(value, JsonOptions),
                value => JsonSerializer.Deserialize<List<string>>(value, JsonOptions) ?? new()
            )
            .Metadata.SetValueComparer(
                new ValueComparer<List<string>>(
                    (left, right) =>
                        JsonSerializer.Serialize(left, JsonOptions)
                        == JsonSerializer.Serialize(right, JsonOptions),
                    value =>
                        JsonSerializer
                            .Serialize(value, JsonOptions)
                            .GetHashCode(StringComparison.Ordinal),
                    value =>
                        JsonSerializer.Deserialize<List<string>>(
                            JsonSerializer.Serialize(value, JsonOptions),
                            JsonOptions
                        )!
                )
            );
        builder.HasIndex(x => new { x.ProductId, x.CreatedAt });
        builder.HasIndex(x => x.GenerationId).IsUnique();
        builder
            .HasIndex(x => new { x.ProductId, x.Language })
            .IsUnique()
            .HasFilter("[IsCurrent] = 1");
    }
}
