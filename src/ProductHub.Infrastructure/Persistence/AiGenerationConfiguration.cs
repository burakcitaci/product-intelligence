using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductHub.Domain.Content;
using ProductHub.Domain.Products;

namespace ProductHub.Infrastructure.Persistence;

internal sealed class AiGenerationConfiguration : IEntityTypeConfiguration<AiGeneration>
{
    public void Configure(EntityTypeBuilder<AiGeneration> builder)
    {
        builder.ToTable("AiGenerations");
        builder.HasKey(x => x.Id);
        builder
            .HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(x => x.Provider).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(x => x.Model).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PromptVersion).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(x => x.Language).HasMaxLength(10).IsUnicode(false).IsRequired();
        builder
            .Property(x => x.GroundingMode)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsUnicode(false);
        builder
            .Property(x => x.RetrievalStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsUnicode(false);
        builder.Property(x => x.RetrievedSourcesJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.RequestedTopK);
        builder.Property(x => x.RetrievalErrorCode).HasMaxLength(100).IsUnicode(false);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
        builder.Property(x => x.ErrorCode).HasMaxLength(100).IsUnicode(false);
        builder.Property(x => x.CorrelationId).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(x => x.StartedAt).HasPrecision(3);
        builder.Property(x => x.CompletedAt).HasPrecision(3);
        builder.HasIndex(x => new { x.ProductId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.StartedAt });
    }
}
