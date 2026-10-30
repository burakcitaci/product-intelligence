using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;
using ProductHub.Application.Abstractions;
using ProductHub.Application.Agents;
using ProductHub.Application.Content;
using ProductHub.Application.Knowledge;
using ProductHub.Infrastructure.Ai;
using ProductHub.Infrastructure.Knowledge;
using ProductHub.Infrastructure.Persistence;

namespace ProductHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("ProductHub")
            ?? throw new InvalidOperationException(
                "Connection string 'ProductHub' is not configured."
            );
        services.AddDbContext<ProductHubDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null)
            )
        );
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<IContentGenerationStore, ContentGenerationStore>();
        services.AddScoped<IKnowledgeDocumentStore, KnowledgeDocumentStore>();
        services.AddScoped<IAgentWorkflowStore, AgentWorkflowStore>();
        services
            .AddOptions<AiSettings>()
            .Bind(configuration.GetSection(AiSettings.SectionName))
            .Validate(x => x.Provider == "Ollama", "Only the Ollama provider is supported in M2.")
            .Validate(
                x => x.Endpoint.Scheme is "http" or "https",
                "AI endpoint must be HTTP or HTTPS."
            )
            .Validate(x => !string.IsNullOrWhiteSpace(x.Model), "AI model is required.")
            .Validate(
                x => x.Models.Length > 0 && x.Models.Contains(x.Model),
                "AI models must include the default model."
            )
            .Validate(
                x => x.TimeoutSeconds is >= 1 and <= 600,
                "AI timeout must be between 1 and 600 seconds."
            )
            .ValidateOnStart();
        services.AddSingleton<IChatClient>(provider =>
        {
            var ai = provider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<AiSettings>>()
                .Value;
            return new OllamaApiClient(ai.Endpoint, ai.Model);
        });
        services.AddScoped<IContentGenerator, OllamaContentGenerator>();
        services.AddScoped<IAgentPlanner, OllamaAgentPlanner>();
        services
            .AddOptions<AgentSettings>()
            .Bind(configuration.GetSection(AgentSettings.SectionName))
            .Validate(
                x => x.MaxSteps is >= 1 and <= 10,
                "Agent max steps must be between 1 and 10."
            )
            .Validate(
                x => x.TimeoutSeconds is >= 1 and <= 300,
                "Agent timeout must be between 1 and 300 seconds."
            )
            .ValidateOnStart();
        services
            .AddOptions<RagSettings>()
            .Bind(configuration.GetSection(RagSettings.SectionName))
            .Validate(x => x.VectorSize > 0, "RAG vector size must be positive.")
            .Validate(
                x => x.ChunkSize > 100 && x.ChunkOverlap >= 0 && x.ChunkOverlap < x.ChunkSize,
                "RAG chunk settings are invalid."
            )
            .ValidateOnStart();
        services.AddSingleton<IDocumentTextExtractor, LocalDocumentTextExtractor>();
        services.AddHttpClient<ITextEmbeddingGenerator, OllamaEmbeddingGenerator>(
            (provider, client) =>
                client.BaseAddress = provider
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<RagSettings>>()
                    .Value.OllamaEndpoint
        );
        services.AddHttpClient<IVectorStore, QdrantVectorStore>(
            (provider, client) =>
                client.BaseAddress = provider
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<RagSettings>>()
                    .Value.QdrantEndpoint
        );
        return services;
    }
}
