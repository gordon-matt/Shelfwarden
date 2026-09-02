using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Shelfwarden.Data.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddUniverseTimelineDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UniverseBooks_UniverseId_TimelineOrder",
                table: "UniverseBooks");

            migrationBuilder.CreateTable(
                name: "TimelineDates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UniverseId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimelineDates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimelineDates_Universes_UniverseId",
                        column: x => x.UniverseId,
                        principalTable: "Universes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            // Data migration: every existing UniverseBook row gets its own slot, carrying its old
            // free-text date and order across untouched. Nothing merges at migration time —
            // sharing only starts once an admin gives two books the same date going forward.
            migrationBuilder.Sql(
                """
                INSERT INTO TimelineDates (UniverseId, Date, `Order`)
                SELECT UniverseId, TimelineDate, TimelineOrder FROM UniverseBooks;
                """);

            migrationBuilder.DropColumn(
                name: "TimelineDate",
                table: "UniverseBooks");

            migrationBuilder.RenameColumn(
                name: "TimelineOrder",
                table: "UniverseBooks",
                newName: "TimelineDateId");

            // The rename above preserves the old TimelineOrder integer in the (now renamed)
            // TimelineDateId column. Use it to look up the slot just inserted for the same
            // universe/order and swap it for that slot's real id. MySQL's UPDATE...JOIN is used
            // instead of a correlated subquery, which historically has restrictions when the
            // subquery references the very table being updated.
            migrationBuilder.Sql(
                """
                UPDATE UniverseBooks ub
                JOIN TimelineDates td ON td.UniverseId = ub.UniverseId AND td.`Order` = ub.TimelineDateId
                SET ub.TimelineDateId = td.Id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_UniverseBooks_TimelineDateId",
                table: "UniverseBooks",
                column: "TimelineDateId");

            migrationBuilder.CreateIndex(
                name: "IX_TimelineDates_UniverseId_Order",
                table: "TimelineDates",
                columns: new[] { "UniverseId", "Order" });

            migrationBuilder.AddForeignKey(
                name: "FK_UniverseBooks_TimelineDates_TimelineDateId",
                table: "UniverseBooks",
                column: "TimelineDateId",
                principalTable: "TimelineDates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UniverseBooks_TimelineDates_TimelineDateId",
                table: "UniverseBooks");

            migrationBuilder.DropTable(
                name: "TimelineDates");

            migrationBuilder.DropIndex(
                name: "IX_UniverseBooks_TimelineDateId",
                table: "UniverseBooks");

            migrationBuilder.RenameColumn(
                name: "TimelineDateId",
                table: "UniverseBooks",
                newName: "TimelineOrder");

            migrationBuilder.AddColumn<string>(
                name: "TimelineDate",
                table: "UniverseBooks",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UniverseBooks_UniverseId_TimelineOrder",
                table: "UniverseBooks",
                columns: new[] { "UniverseId", "TimelineOrder" });
        }
    }
}
