using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductHub.Domain.Knowledge;
using ProductHub.Domain.Products;

namespace ProductHub.Infrastructure.Persistence;

internal sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> b)
    {
        b.ToTable("KnowledgeDocuments");
        b.HasKey(x => x.Id);
        b.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.ProductId, x.CreatedAt });
        b.HasIndex(x => new { x.ProductId, x.Sha256 }).IsUnique();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ErrorCode).HasMaxLength(100);
        b.Property(x => x.ExtractionVersion).HasMaxLength(100).IsRequired();
        b.Property(x => x.SupersededByDocumentId).IsConcurrencyToken();
        b.HasMany(x => x.Chunks)
            .WithOne(x => x.Document)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Chunks).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> b)
    {
        b.ToTable("DocumentChunks");
        b.HasKey(x => x.Id);
        b.Property(x => x.Text).HasColumnType("nvarchar(max)").IsRequired();
        b.HasIndex(x => new { x.DocumentId, x.Ordinal }).IsUnique();
    }
}
