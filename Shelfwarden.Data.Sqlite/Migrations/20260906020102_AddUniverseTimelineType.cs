using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddUniverseTimelineType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Date",
                schema: "app",
                table: "TimelineDates",
                newName: "Name");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "app",
                table: "TimelineDates",
                type: "TEXT",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<int>(
                name: "TimelineType",
                schema: "app",
                table: "Universes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimelineType",
                schema: "app",
                table: "Universes");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "app",
                table: "TimelineDates",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "Name",
                schema: "app",
                table: "TimelineDates",
                newName: "Date");
        }
    }
}
