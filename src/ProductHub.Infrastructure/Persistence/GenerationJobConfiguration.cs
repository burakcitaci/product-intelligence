using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductHub.Domain.Content;

namespace ProductHub.Infrastructure.Persistence;

internal sealed class GenerationJobConfiguration : IEntityTypeConfiguration<GenerationJob>
{
    public void Configure(EntityTypeBuilder<GenerationJob> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CommandJson).IsRequired();
        builder.Property(x => x.RequestedBy).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Stage).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ErrorCode).HasMaxLength(100);
        builder.Property(x => x.ErrorMessage).HasMaxLength(1000);
        builder.HasIndex(x => new { x.ProductId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
        builder
            .HasOne<ProductHub.Domain.Products.Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
