using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Npgsql.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelineDateAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UniverseBooks_TimelineDates_TimelineDateId",
                schema: "app",
                table: "UniverseBooks");

            migrationBuilder.DropIndex(
                name: "IX_UniverseBooks_TimelineDateId",
                schema: "app",
                table: "UniverseBooks");

            migrationBuilder.AlterColumn<int>(
                name: "TimelineDateId",
                schema: "app",
                table: "UniverseBooks",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "Order",
                schema: "app",
                table: "UniverseBooks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Date",
                schema: "app",
                table: "TimelineDates",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UniverseBooks_TimelineDateId_Order",
                schema: "app",
                table: "UniverseBooks",
                columns: new[] { "TimelineDateId", "Order" });

            migrationBuilder.AddForeignKey(
                name: "FK_UniverseBooks_TimelineDates_TimelineDateId",
                schema: "app",
                table: "UniverseBooks",
                column: "TimelineDateId",
                principalSchema: "app",
                principalTable: "TimelineDates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UniverseBooks_TimelineDates_TimelineDateId",
                schema: "app",
                table: "UniverseBooks");

            migrationBuilder.DropIndex(
                name: "IX_UniverseBooks_TimelineDateId_Order",
                schema: "app",
                table: "UniverseBooks");

            migrationBuilder.DropColumn(
                name: "Order",
                schema: "app",
                table: "UniverseBooks");

            migrationBuilder.AlterColumn<int>(
                name: "TimelineDateId",
                schema: "app",
                table: "UniverseBooks",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Date",
                schema: "app",
                table: "TimelineDates",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.CreateIndex(
                name: "IX_UniverseBooks_TimelineDateId",
                schema: "app",
                table: "UniverseBooks",
                column: "TimelineDateId");

            migrationBuilder.AddForeignKey(
                name: "FK_UniverseBooks_TimelineDates_TimelineDateId",
                schema: "app",
                table: "UniverseBooks",
                column: "TimelineDateId",
                principalSchema: "app",
                principalTable: "TimelineDates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
