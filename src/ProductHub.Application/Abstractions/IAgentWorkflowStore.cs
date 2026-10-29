using ProductHub.Domain.Agents;
using ProductHub.Domain.Products;

namespace ProductHub.Application.Abstractions;

public interface IAgentWorkflowStore
{
    Task AddAsync(AgentWorkflow workflow, CancellationToken cancellationToken);
    Task<AgentWorkflow?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Product?> GetProductAsync(Guid id, bool forUpdate, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
