using Microsoft.Extensions.Options;
using ProductHub.Application.Abstractions;
using ProductHub.Application.Agents;
using ProductHub.Domain.Agents;
using ProductHub.Domain.Products;

namespace ProductHub.UnitTests.Agents;

public sealed class AgentWorkflowServiceTests
{
    [Fact]
    public async Task StartAsync_ReadOnlyPlan_CompletesWithoutApproval()
    {
        var store = new FakeStore();
        var service = Create(
            store,
            new AgentPlan([new("product.lookup"), new("product.validate")])
        );
        var result = await service.StartAsync(
            new(store.Product.Id, "Inspect product quality"),
            CancellationToken.None
        );
        Assert.Equal(AgentWorkflowStatus.Completed, result.Status);
        Assert.Equal(2, result.Results.Count);
        Assert.Null(result.Proposal);
    }

    [Fact]
    public async Task StartAsync_UpdatePlan_WaitsForHumanApproval()
    {
        var store = new FakeStore();
        var service = Create(
            store,
            new AgentPlan([
                new(
                    "product.propose_update",
                    new Dictionary<string, string> { ["name"] = "Improved Bottle" }
                ),
            ])
        );
        var result = await service.StartAsync(
            new(store.Product.Id, "Rename the product to Improved Bottle"),
            CancellationToken.None
        );
        Assert.Equal(AgentWorkflowStatus.AwaitingApproval, result.Status);
        Assert.Equal("Improved Bottle", result.Proposal!.Name);
        Assert.Equal("Bottle", store.Product.Name);
    }

    [Fact]
    public async Task ApproveAsync_AppliesProposalExactlyOnce()
    {
        var store = new FakeStore();
        var service = Create(
            store,
            new AgentPlan([
                new(
                    "product.propose_update",
                    new Dictionary<string, string> { ["brand"] = "Contoso" }
                ),
            ])
        );
        var started = await service.StartAsync(
            new(store.Product.Id, "Change brand to Contoso"),
            CancellationToken.None
        );
        var approved = await service.ApproveAsync(started.Id, CancellationToken.None);
        Assert.Equal(AgentWorkflowStatus.Completed, approved.Status);
        Assert.Equal("Contoso", store.Product.Brand);
        await Assert.ThrowsAsync<ProductHub.Application.Products.ConflictException>(() =>
            service.ApproveAsync(started.Id, CancellationToken.None)
        );
    }

    [Fact]
    public async Task StartAsync_UnauthorizedTool_FailsClosed()
    {
        var store = new FakeStore();
        var service = Create(
            store,
            new AgentPlan([new("database.execute", new Dictionary<string, string>())])
        );
        var error = await Assert.ThrowsAsync<AgentWorkflowException>(() =>
            service.StartAsync(new(store.Product.Id, "Run arbitrary SQL"), CancellationToken.None)
        );
        Assert.Equal("tool-not-authorized", error.ErrorCode);
        Assert.Equal(AgentWorkflowStatus.Failed, store.Workflow!.Status);
    }

    private static AgentWorkflowService Create(FakeStore store, AgentPlan plan) =>
        new(
            store,
            new FakePlanner(plan),
            Options.Create(
                new AgentSettings
                {
                    MaxSteps = 5,
                    TimeoutSeconds = 10,
                    MaxArgumentLength = 100,
                }
            )
        );

    private sealed class FakePlanner(AgentPlan plan) : IAgentPlanner
    {
        public Task<AgentPlan> CreatePlanAsync(
            AgentPlanningContext context,
            CancellationToken cancellationToken
        ) => Task.FromResult(plan);
    }

    private sealed class FakeStore : IAgentWorkflowStore
    {
        public Product Product { get; } =
            Product.Create(
                "SKU-1",
                "Bottle",
                "Acme",
                attributes: [new ProductHub.Domain.Products.ProductAttribute("Volume", "750", "ml")]
            );
        public AgentWorkflow? Workflow { get; private set; }

        public Task AddAsync(AgentWorkflow workflow, CancellationToken cancellationToken)
        {
            Workflow = workflow;
            return Task.CompletedTask;
        }

        public Task<AgentWorkflow?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Workflow?.Id == id ? Workflow : null);

        public Task<Product?> GetProductAsync(
            Guid id,
            bool forUpdate,
            CancellationToken cancellationToken
        ) => Task.FromResult<Product?>(Product.Id == id ? Product : null);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
