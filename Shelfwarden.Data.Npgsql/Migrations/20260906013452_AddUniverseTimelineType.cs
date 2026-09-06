using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Npgsql.Migrations
{
    /// <inheritdoc />
    public partial class AddUniverseTimelineType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TimelineType",
                schema: "app",
                table: "Universes",
                type: "integer",
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
        }
    }
}
