using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sql.Migrations
{
    /// <inheritdoc />
    public partial class AddShelfImportOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AlwaysIgnoreAuthor",
                schema: "app",
                table: "Shelves",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AlwaysIgnoreGenres",
                schema: "app",
                table: "Shelves",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AlwaysIgnoreTags",
                schema: "app",
                table: "Shelves",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AlwaysUseFileNameForTitle",
                schema: "app",
                table: "Shelves",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AssignNewBooksToCollection",
                schema: "app",
                table: "Shelves",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DirectoryStructure",
                schema: "app",
                table: "Shelves",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NewBooksCollectionName",
                schema: "app",
                table: "Shelves",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlwaysIgnoreAuthor",
                schema: "app",
                table: "Shelves");

            migrationBuilder.DropColumn(
                name: "AlwaysIgnoreGenres",
                schema: "app",
                table: "Shelves");

            migrationBuilder.DropColumn(
                name: "AlwaysIgnoreTags",
                schema: "app",
                table: "Shelves");

            migrationBuilder.DropColumn(
                name: "AlwaysUseFileNameForTitle",
                schema: "app",
                table: "Shelves");

            migrationBuilder.DropColumn(
                name: "AssignNewBooksToCollection",
                schema: "app",
                table: "Shelves");

            migrationBuilder.DropColumn(
                name: "DirectoryStructure",
                schema: "app",
                table: "Shelves");

            migrationBuilder.DropColumn(
                name: "NewBooksCollectionName",
                schema: "app",
                table: "Shelves");
        }
    }
}
