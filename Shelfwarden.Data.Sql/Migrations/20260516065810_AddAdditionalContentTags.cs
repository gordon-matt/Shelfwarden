using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sql.Migrations
{
    /// <inheritdoc />
    public partial class AddAdditionalContentTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdditionalContentTags",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdditionalContentTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdditionalContentItemTags",
                schema: "app",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdditionalContentItemTags", x => new { x.ItemId, x.TagId });
                    table.ForeignKey(
                        name: "FK_AdditionalContentItemTags_AdditionalContentTags_TagId",
                        column: x => x.TagId,
                        principalSchema: "app",
                        principalTable: "AdditionalContentTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AdditionalContentItemTags_AdditionalContent_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "app",
                        principalTable: "AdditionalContent",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalContentItemTags_ItemId_TagId",
                schema: "app",
                table: "AdditionalContentItemTags",
                columns: new[] { "ItemId", "TagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalContentItemTags_TagId",
                schema: "app",
                table: "AdditionalContentItemTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalContentTags_NormalizedName",
                schema: "app",
                table: "AdditionalContentTags",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdditionalContentItemTags",
                schema: "app");

            migrationBuilder.DropTable(
                name: "AdditionalContentTags",
                schema: "app");
        }
    }
}
