using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Shelfwarden.Data.Npgsql.Migrations
{
    /// <inheritdoc />
    public partial class AddUniverseTimelineDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UniverseBooks_UniverseId_TimelineOrder",
                schema: "app",
                table: "UniverseBooks");

            migrationBuilder.CreateTable(
                name: "TimelineDates",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UniverseId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimelineDates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimelineDates_Universes_UniverseId",
                        column: x => x.UniverseId,
                        principalSchema: "app",
                        principalTable: "Universes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Data migration: every existing UniverseBook row gets its own slot, carrying its old
            // free-text date and order across untouched. Nothing merges at migration time —
            // sharing only starts once an admin gives two books the same date going forward.
            migrationBuilder.Sql(
                """
                INSERT INTO "app"."TimelineDates" ("UniverseId", "Date", "Order")
                SELECT "UniverseId", "TimelineDate", "TimelineOrder" FROM "app"."UniverseBooks";
                """);

            migrationBuilder.DropColumn(
                name: "TimelineDate",
                schema: "app",
                table: "UniverseBooks");

            migrationBuilder.RenameColumn(
                name: "TimelineOrder",
                schema: "app",
                table: "UniverseBooks",
                newName: "TimelineDateId");

            // The rename above preserves the old TimelineOrder integer in the (now renamed)
            // TimelineDateId column. Use it to look up the slot just inserted for the same
            // universe/order and swap it for that slot's real id.
            migrationBuilder.Sql(
                """
                UPDATE "app"."UniverseBooks" AS ub
                SET "TimelineDateId" = (
                    SELECT td."Id" FROM "app"."TimelineDates" td
                    WHERE td."UniverseId" = ub."UniverseId" AND td."Order" = ub."TimelineDateId"
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_UniverseBooks_TimelineDateId",
                schema: "app",
                table: "UniverseBooks",
                column: "TimelineDateId");

            migrationBuilder.CreateIndex(
                name: "IX_TimelineDates_UniverseId_Order",
                schema: "app",
                table: "TimelineDates",
                columns: new[] { "UniverseId", "Order" });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UniverseBooks_TimelineDates_TimelineDateId",
                schema: "app",
                table: "UniverseBooks");

            migrationBuilder.DropTable(
                name: "TimelineDates",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "IX_UniverseBooks_TimelineDateId",
                schema: "app",
                table: "UniverseBooks");

            migrationBuilder.RenameColumn(
                name: "TimelineDateId",
                schema: "app",
                table: "UniverseBooks",
                newName: "TimelineOrder");

            migrationBuilder.AddColumn<string>(
                name: "TimelineDate",
                schema: "app",
                table: "UniverseBooks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UniverseBooks_UniverseId_TimelineOrder",
                schema: "app",
                table: "UniverseBooks",
                columns: new[] { "UniverseId", "TimelineOrder" });
        }
    }
}
