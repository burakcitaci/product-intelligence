using Microsoft.EntityFrameworkCore;
using ProductHub.Domain.Agents;
using ProductHub.Domain.Catalogs;
using ProductHub.Domain.Content;
using ProductHub.Domain.Knowledge;
using ProductHub.Domain.Products;
using ProductHub.Domain.Users;

namespace ProductHub.Infrastructure.Persistence;

public sealed class ProductHubDbContext(DbContextOptions<ProductHubDbContext> options)
    : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Catalog> Catalogs => Set<Catalog>();
    public DbSet<ContentDraft> ContentDrafts => Set<ContentDraft>();
    public DbSet<GenerationJob> GenerationJobs => Set<GenerationJob>();
    public DbSet<AiGeneration> AiGenerations => Set<AiGeneration>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<AgentWorkflow> AgentWorkflows => Set<AgentWorkflow>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProductHubDbContext).Assembly);
}
