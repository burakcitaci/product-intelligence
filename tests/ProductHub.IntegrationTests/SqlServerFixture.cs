using Microsoft.EntityFrameworkCore;
using ProductHub.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace ProductHub.IntegrationTests;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer container = new MsSqlBuilder().Build();
    public string ConnectionString => container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        var options = new DbContextOptionsBuilder<ProductHubDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        await using var db = new ProductHubDbContext(options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}
