using Microsoft.Extensions.DependencyInjection;
using ProductHub.Application.Agents;
using ProductHub.Application.Catalogs;
using ProductHub.Application.Content;
using ProductHub.Application.Knowledge;
using ProductHub.Application.Products;

namespace ProductHub.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services) =>
        services
            .AddScoped<ProductService>()
            .AddScoped<CatalogService>()
            .AddScoped<ContentGenerationService>()
            .AddScoped<DocumentIngestionService>()
            .AddScoped<KnowledgeSearchService>()
            .AddScoped<AgentWorkflowService>();
}
