using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductHub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ProductHubDbContext))]
[Migration("20261029120000_AddRagGroundingAudit")]
public partial class AddRagGroundingAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_KnowledgeDocuments_Sha256",
            table: "KnowledgeDocuments"
        );
        migrationBuilder.AddColumn<string>(
            name: "GroundingMode",
            table: "AiGenerations",
            type: "varchar(30)",
            unicode: false,
            maxLength: 30,
            nullable: false,
            defaultValue: "FactsOnly"
        );
        migrationBuilder.AddColumn<string>(
            name: "RetrievalStatus",
            table: "AiGenerations",
            type: "varchar(30)",
            unicode: false,
            maxLength: 30,
            nullable: false,
            defaultValue: "NotRequested"
        );
        migrationBuilder.AddColumn<long>(
            name: "RetrievalDurationMs",
            table: "AiGenerations",
            type: "bigint",
            nullable: true
        );
        migrationBuilder.AddColumn<int>(
            name: "RetrievedChunkCount",
            table: "AiGenerations",
            type: "int",
            nullable: false,
            defaultValue: 0
        );
        migrationBuilder.AddColumn<int>(
            name: "RetrievedDocumentCount",
            table: "AiGenerations",
            type: "int",
            nullable: false,
            defaultValue: 0
        );
        migrationBuilder.AddColumn<string>(
            name: "RetrievedSourcesJson",
            table: "AiGenerations",
            type: "nvarchar(max)",
            nullable: true
        );
        migrationBuilder.AddColumn<string>(
            name: "RetrievalErrorCode",
            table: "AiGenerations",
            type: "varchar(100)",
            unicode: false,
            maxLength: 100,
            nullable: true
        );

        migrationBuilder.AddColumn<Guid>(
            name: "ProductId",
            table: "KnowledgeDocuments",
            type: "uniqueidentifier",
            nullable: true
        );
        migrationBuilder.CreateIndex(
            name: "IX_KnowledgeDocuments_ProductId_CreatedAt",
            table: "KnowledgeDocuments",
            columns: new[] { "ProductId", "CreatedAt" }
        );
        migrationBuilder.CreateIndex(
            name: "IX_KnowledgeDocuments_ProductId_Sha256",
            table: "KnowledgeDocuments",
            columns: new[] { "ProductId", "Sha256" },
            unique: true,
            filter: "[ProductId] IS NOT NULL"
        );
        migrationBuilder.AddForeignKey(
            name: "FK_KnowledgeDocuments_Products_ProductId",
            table: "KnowledgeDocuments",
            column: "ProductId",
            principalTable: "Products",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_KnowledgeDocuments_Products_ProductId",
            table: "KnowledgeDocuments"
        );
        migrationBuilder.DropIndex(
            name: "IX_KnowledgeDocuments_ProductId_CreatedAt",
            table: "KnowledgeDocuments"
        );
        migrationBuilder.DropIndex(
            name: "IX_KnowledgeDocuments_ProductId_Sha256",
            table: "KnowledgeDocuments"
        );
        migrationBuilder.DropColumn(name: "ProductId", table: "KnowledgeDocuments");
        migrationBuilder.DropColumn(name: "GroundingMode", table: "AiGenerations");
        migrationBuilder.DropColumn(name: "RetrievalStatus", table: "AiGenerations");
        migrationBuilder.DropColumn(name: "RetrievalDurationMs", table: "AiGenerations");
        migrationBuilder.DropColumn(name: "RetrievedChunkCount", table: "AiGenerations");
        migrationBuilder.DropColumn(name: "RetrievedDocumentCount", table: "AiGenerations");
        migrationBuilder.DropColumn(name: "RetrievedSourcesJson", table: "AiGenerations");
        migrationBuilder.DropColumn(name: "RetrievalErrorCode", table: "AiGenerations");
        migrationBuilder.CreateIndex(
            name: "IX_KnowledgeDocuments_Sha256",
            table: "KnowledgeDocuments",
            column: "Sha256",
            unique: true
        );
    }
}
