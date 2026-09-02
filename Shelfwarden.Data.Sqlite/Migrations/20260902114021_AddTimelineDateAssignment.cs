using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sqlite.Migrations
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
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "Order",
                schema: "app",
                table: "UniverseBooks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Existing rows are re-pointed by the BackfillTimelineDateAssignment migration that
            // follows. It has to be a separate migration: SQLite implements these column changes
            // by rebuilding the table at the end of the migration, so any data statements here
            // would run against the old, still-NOT NULL definition.
            migrationBuilder.AlterColumn<string>(
                name: "Date",
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

            // The old schema has nowhere to put an unscheduled book, so going back drops those
            // memberships. The books themselves are untouched.
            migrationBuilder.Sql(
                """
                DELETE FROM "UniverseBooks" WHERE "TimelineDateId" IS NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "TimelineDateId",
                schema: "app",
                table: "UniverseBooks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Date",
                schema: "app",
                table: "TimelineDates",
                type: "TEXT",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
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
