using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sql.Migrations
{
    /// <inheritdoc />
    public partial class AddShelfAutoFetchMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoFetchOnlineMetadata",
                schema: "app",
                table: "Shelves",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoFetchOnlineMetadata",
                schema: "app",
                table: "Shelves");
        }
    }
}
