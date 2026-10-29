using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductHub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ProductHubDbContext))]
[Migration("20261029093000_AddCurrentContentDraft")]
public partial class AddCurrentContentDraft : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsCurrent",
            table: "ContentDrafts",
            type: "bit",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.Sql(
            """
            WITH RankedApproved AS
            (
                SELECT Id,
                       ROW_NUMBER() OVER (
                           PARTITION BY ProductId, Language
                           ORDER BY CreatedAt DESC, Id DESC
                       ) AS VersionRank
                FROM ContentDrafts
                WHERE Status = 'Approved'
            )
            UPDATE drafts
            SET IsCurrent = 1
            FROM ContentDrafts AS drafts
            INNER JOIN RankedApproved AS ranked ON ranked.Id = drafts.Id
            WHERE ranked.VersionRank = 1;
            """
        );

        migrationBuilder.CreateIndex(
            name: "IX_ContentDrafts_ProductId_Language",
            table: "ContentDrafts",
            columns: new[] { "ProductId", "Language" },
            unique: true,
            filter: "[IsCurrent] = 1"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ContentDrafts_ProductId_Language",
            table: "ContentDrafts"
        );

        migrationBuilder.DropColumn(name: "IsCurrent", table: "ContentDrafts");
    }
}
