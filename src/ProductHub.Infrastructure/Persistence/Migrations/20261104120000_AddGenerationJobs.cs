using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ProductHub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ProductHubDbContext))]
[Migration("20261104120000_AddGenerationJobs")]
public sealed class AddGenerationJobs : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable(
            "GenerationJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IdempotencyKey = table.Column<string>(
                    type: "nvarchar(100)",
                    maxLength: 100,
                    nullable: false
                ),
                CommandJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                RequestedBy = table.Column<string>(
                    type: "nvarchar(200)",
                    maxLength: 200,
                    nullable: false
                ),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Stage = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                LeaseExpiresAt = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: true
                ),
                CancellationRequested = table.Column<bool>(type: "bit", nullable: false),
                DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ErrorCode = table.Column<string>(
                    type: "nvarchar(100)",
                    maxLength: 100,
                    nullable: true
                ),
                ErrorMessage = table.Column<string>(
                    type: "nvarchar(1000)",
                    maxLength: 1000,
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GenerationJobs", x => x.Id);
                table.ForeignKey(
                    "FK_GenerationJobs_Products_ProductId",
                    x => x.ProductId,
                    "Products",
                    "Id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );
        m.CreateIndex(
            "IX_GenerationJobs_ProductId_IdempotencyKey",
            "GenerationJobs",
            new[] { "ProductId", "IdempotencyKey" },
            unique: true
        );
        m.CreateIndex(
            "IX_GenerationJobs_Status_CreatedAt",
            "GenerationJobs",
            new[] { "Status", "CreatedAt" }
        );
    }

    protected override void Down(MigrationBuilder m) => m.DropTable("GenerationJobs");
}
