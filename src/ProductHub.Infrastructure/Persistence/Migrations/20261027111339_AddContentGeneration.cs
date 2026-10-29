using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContentGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiGenerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(
                        type: "varchar(100)",
                        unicode: false,
                        maxLength: 100,
                        nullable: false
                    ),
                    Provider = table.Column<string>(
                        type: "varchar(50)",
                        unicode: false,
                        maxLength: 50,
                        nullable: false
                    ),
                    Model = table.Column<string>(
                        type: "nvarchar(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    PromptVersion = table.Column<string>(
                        type: "varchar(100)",
                        unicode: false,
                        maxLength: 100,
                        nullable: false
                    ),
                    Language = table.Column<string>(
                        type: "varchar(10)",
                        unicode: false,
                        maxLength: 10,
                        nullable: false
                    ),
                    StartedAt = table.Column<DateTimeOffset>(
                        type: "datetimeoffset(3)",
                        precision: 3,
                        nullable: false
                    ),
                    CompletedAt = table.Column<DateTimeOffset>(
                        type: "datetimeoffset(3)",
                        precision: 3,
                        nullable: true
                    ),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    InputTokens = table.Column<int>(type: "int", nullable: true),
                    OutputTokens = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(
                        type: "varchar(30)",
                        unicode: false,
                        maxLength: 30,
                        nullable: false
                    ),
                    ErrorCode = table.Column<string>(
                        type: "varchar(100)",
                        unicode: false,
                        maxLength: 100,
                        nullable: true
                    ),
                    CorrelationId = table.Column<string>(
                        type: "varchar(100)",
                        unicode: false,
                        maxLength: 100,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGenerations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiGenerations_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "ContentDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Language = table.Column<string>(
                        type: "varchar(10)",
                        unicode: false,
                        maxLength: 10,
                        nullable: false
                    ),
                    Title = table.Column<string>(
                        type: "nvarchar(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    ShortDescription = table.Column<string>(
                        type: "nvarchar(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    LongDescription = table.Column<string>(
                        type: "nvarchar(max)",
                        maxLength: 8000,
                        nullable: false
                    ),
                    Highlights = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(
                        type: "varchar(30)",
                        unicode: false,
                        maxLength: 30,
                        nullable: false
                    ),
                    CreatedAt = table.Column<DateTimeOffset>(
                        type: "datetimeoffset(3)",
                        precision: 3,
                        nullable: false
                    ),
                    GenerationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentDrafts_AiGenerations_GenerationId",
                        column: x => x.GenerationId,
                        principalTable: "AiGenerations",
                        principalColumn: "Id"
                    );
                    table.ForeignKey(
                        name: "FK_ContentDrafts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerations_ProductId_IdempotencyKey",
                table: "AiGenerations",
                columns: new[] { "ProductId", "IdempotencyKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerations_Status_StartedAt",
                table: "AiGenerations",
                columns: new[] { "Status", "StartedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ContentDrafts_GenerationId",
                table: "ContentDrafts",
                column: "GenerationId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ContentDrafts_ProductId_CreatedAt",
                table: "ContentDrafts",
                columns: new[] { "ProductId", "CreatedAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ContentDrafts");

            migrationBuilder.DropTable(name: "AiGenerations");
        }
    }
}
