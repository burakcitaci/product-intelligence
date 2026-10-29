using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace ProductHub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ProductHubDbContext))]
[Migration("202610270001_InitialCatalog")]
public partial class InitialCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Products",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Sku = table.Column<string>(
                    type: "varchar(64)",
                    unicode: false,
                    maxLength: 64,
                    nullable: false
                ),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Brand = table.Column<string>(
                    type: "nvarchar(100)",
                    maxLength: 100,
                    nullable: false
                ),
                CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Attributes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(
                    type: "datetimeoffset(3)",
                    precision: 3,
                    nullable: false
                ),
                UpdatedAt = table.Column<DateTimeOffset>(
                    type: "datetimeoffset(3)",
                    precision: 3,
                    nullable: false
                ),
                RowVersion = table.Column<byte[]>(
                    type: "rowversion",
                    rowVersion: true,
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Products", x => x.Id);
                table.CheckConstraint(
                    "CK_Products_Brand_NotEmpty",
                    "LEN(LTRIM(RTRIM([Brand]))) > 0"
                );
                table.CheckConstraint("CK_Products_Name_NotEmpty", "LEN(LTRIM(RTRIM([Name]))) > 0");
                table.CheckConstraint("CK_Products_Sku_NotEmpty", "LEN(LTRIM(RTRIM([Sku]))) > 0");
            }
        );
        migrationBuilder.CreateIndex(
            name: "IX_Products_Brand_Name",
            table: "Products",
            columns: new[] { "Brand", "Name" }
        );
        migrationBuilder.CreateIndex(
            name: "IX_Products_Sku",
            table: "Products",
            column: "Sku",
            unique: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "Products");
}
