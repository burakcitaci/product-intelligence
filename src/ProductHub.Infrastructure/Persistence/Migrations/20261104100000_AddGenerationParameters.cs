using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ProductHub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ProductHubDbContext))]
[Migration("20261104100000_AddGenerationParameters")]
public sealed class AddGenerationParameters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "GenerationParametersJson",
            table: "AiGenerations",
            type: "nvarchar(max)",
            nullable: true
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "GenerationParametersJson", table: "AiGenerations");
}
