using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ProductHub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ProductHubDbContext))]
[Migration("20261104110000_AddDraftClaimReview")]
public sealed class AddDraftClaimReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "ClaimReviewJson",
            table: "ContentDrafts",
            type: "nvarchar(max)",
            nullable: true
        );

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "ClaimReviewJson", table: "ContentDrafts");
}
