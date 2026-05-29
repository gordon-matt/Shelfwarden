using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddAudiobookSectioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChaptersJson",
                schema: "app",
                table: "Audiobooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SectionPlanJson",
                schema: "app",
                table: "Audiobooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SplitByChapter",
                schema: "app",
                table: "Audiobooks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChaptersJson",
                schema: "app",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "SectionPlanJson",
                schema: "app",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "SplitByChapter",
                schema: "app",
                table: "Audiobooks");
        }
    }
}
