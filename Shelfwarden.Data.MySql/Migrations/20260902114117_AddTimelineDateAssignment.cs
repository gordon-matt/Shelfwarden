using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelineDateAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UniverseBooks_TimelineDates_TimelineDateId",
                table: "UniverseBooks");

            migrationBuilder.DropIndex(
                name: "IX_UniverseBooks_TimelineDateId",
                table: "UniverseBooks");

            migrationBuilder.AlterColumn<int>(
                name: "TimelineDateId",
                table: "UniverseBooks",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "UniverseBooks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Date",
                table: "TimelineDates",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UniverseBooks_TimelineDateId_Order",
                table: "UniverseBooks",
                columns: new[] { "TimelineDateId", "Order" });

            migrationBuilder.AddForeignKey(
                name: "FK_UniverseBooks_TimelineDates_TimelineDateId",
                table: "UniverseBooks",
                column: "TimelineDateId",
                principalTable: "TimelineDates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UniverseBooks_TimelineDates_TimelineDateId",
                table: "UniverseBooks");

            migrationBuilder.DropIndex(
                name: "IX_UniverseBooks_TimelineDateId_Order",
                table: "UniverseBooks");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "UniverseBooks");

            migrationBuilder.AlterColumn<int>(
                name: "TimelineDateId",
                table: "UniverseBooks",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Date",
                table: "TimelineDates",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldMaxLength: 50);

            migrationBuilder.CreateIndex(
                name: "IX_UniverseBooks_TimelineDateId",
                table: "UniverseBooks",
                column: "TimelineDateId");

            migrationBuilder.AddForeignKey(
                name: "FK_UniverseBooks_TimelineDates_TimelineDateId",
                table: "UniverseBooks",
                column: "TimelineDateId",
                principalTable: "TimelineDates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
