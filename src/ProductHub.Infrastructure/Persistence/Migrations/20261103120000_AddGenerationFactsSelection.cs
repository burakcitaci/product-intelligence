using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductHub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ProductHubDbContext))]
[Migration("20261103120000_AddGenerationFactsSelection")]
public partial class AddGenerationFactsSelection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<bool>(
            name: "IncludeFacts",
            table: "AiGenerations",
            type: "bit",
            nullable: false,
            defaultValue: true
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "IncludeFacts", table: "AiGenerations");
}
