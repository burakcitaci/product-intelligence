using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using ProductHub.Infrastructure.Persistence;

namespace ProductHub.IntegrationTests;

public sealed class PersistenceModelTests
{
    [Fact]
    public void MigrationSnapshot_MatchesRuntimeModel()
    {
        using var db = new ProductHubDbContext(
            new DbContextOptionsBuilder<ProductHubDbContext>()
                .UseSqlServer(
                    "Server=localhost;Database=ModelOnly;Integrated Security=true;TrustServerCertificate=true"
                )
                .Options
        );
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!;
        var initialized = db.GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot.Model, designTime: true);
        var current = db.GetService<IDesignTimeModel>().Model;
        Assert.False(
            db.GetService<IMigrationsModelDiffer>()
                .HasDifferences(initialized.GetRelationalModel(), current.GetRelationalModel())
        );
        Assert.Contains(
            "20261104120000_AddGenerationJobs",
            db.GetService<IMigrationsAssembly>().Migrations.Keys
        );
    }
}
