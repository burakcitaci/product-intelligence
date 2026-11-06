using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ProductHub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ProductHubDbContext))]
[Migration("20261104130000_AddDocumentRevisions")]
public sealed class AddDocumentRevisions : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>(
            "ExtractionVersion",
            "KnowledgeDocuments",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "legacy"
        );
        m.AddColumn<Guid>(
            "SupersededByDocumentId",
            "KnowledgeDocuments",
            type: "uniqueidentifier",
            nullable: true
        );
        m.AddColumn<DateTimeOffset>(
            "SupersededAt",
            "KnowledgeDocuments",
            type: "datetimeoffset",
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropColumn("ExtractionVersion", "KnowledgeDocuments");
        m.DropColumn("SupersededByDocumentId", "KnowledgeDocuments");
        m.DropColumn("SupersededAt", "KnowledgeDocuments");
    }
}
