using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductHub.Domain.Agents;

namespace ProductHub.Infrastructure.Persistence;

internal sealed class AgentWorkflowConfiguration : IEntityTypeConfiguration<AgentWorkflow>
{
    public void Configure(EntityTypeBuilder<AgentWorkflow> b)
    {
        b.ToTable("AgentWorkflows");
        b.HasKey(x => x.Id);
        b.Property(x => x.Objective).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.PlanJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.ResultsJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.ProposalJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.ErrorCode).HasMaxLength(100);
        b.HasIndex(x => new { x.ProductId, x.CreatedAt });
        b.HasOne<ProductHub.Domain.Products.Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
