using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Npgsql.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelineDateYears : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "YearFrom",
                schema: "app",
                table: "TimelineDates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YearTo",
                schema: "app",
                table: "TimelineDates",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "YearFrom",
                schema: "app",
                table: "TimelineDates");

            migrationBuilder.DropColumn(
                name: "YearTo",
                schema: "app",
                table: "TimelineDates");
        }
    }
}
