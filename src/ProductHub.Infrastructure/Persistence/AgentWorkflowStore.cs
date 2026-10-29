using Microsoft.EntityFrameworkCore;
using ProductHub.Application.Abstractions;
using ProductHub.Domain.Agents;
using ProductHub.Domain.Products;

namespace ProductHub.Infrastructure.Persistence;

internal sealed class AgentWorkflowStore(ProductHubDbContext db) : IAgentWorkflowStore
{
    public async Task AddAsync(AgentWorkflow workflow, CancellationToken ct) =>
        await db.AgentWorkflows.AddAsync(workflow, ct);

    public Task<AgentWorkflow?> GetAsync(Guid id, CancellationToken ct) =>
        db.AgentWorkflows.SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<Product?> GetProductAsync(Guid id, bool forUpdate, CancellationToken ct) =>
        forUpdate
            ? db.Products.SingleOrDefaultAsync(x => x.Id == id, ct)
            : db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
