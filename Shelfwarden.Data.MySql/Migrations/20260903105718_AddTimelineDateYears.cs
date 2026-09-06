using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelineDateYears : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "YearFrom",
                table: "TimelineDates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YearTo",
                table: "TimelineDates",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "YearFrom",
                table: "TimelineDates");

            migrationBuilder.DropColumn(
                name: "YearTo",
                table: "TimelineDates");
        }
    }
}
