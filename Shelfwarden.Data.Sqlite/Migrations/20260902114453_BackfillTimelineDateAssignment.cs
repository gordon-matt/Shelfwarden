using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sqlite.Migrations
{
    /// <summary>
    /// Moves existing timeline data onto the new scheme, where dates are created deliberately and
    /// books are assigned to them. Separate from AddTimelineDateAssignment because SQLite applies
    /// that migration's column changes by rebuilding the table at the very end, so data statements
    /// living alongside them would run against the old definition.
    /// </summary>
    public partial class BackfillTimelineDateAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The old scheme minted a blank slot for every book added to a universe. A blank slot
            // isn't a date anyone chose, so those books go back to being unscheduled.
            migrationBuilder.Sql(
                """
                UPDATE "UniverseBooks"
                SET "TimelineDateId" = NULL
                WHERE "TimelineDateId" IN (
                    SELECT "Id" FROM "TimelineDates" WHERE TRIM("Date") = ''
                );
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM "TimelineDates" WHERE TRIM("Date") = '';
                """);

            // Ordering used to come from the slot, one book per slot. Books sharing a date now need
            // a position within it, so number each date's books by the order they were added.
            migrationBuilder.Sql(
                """
                UPDATE "UniverseBooks"
                SET "Order" = (
                    SELECT COUNT(*) FROM "UniverseBooks" prior
                    WHERE prior."UniverseId" = "UniverseBooks"."UniverseId"
                      AND ((prior."TimelineDateId" IS NULL AND "UniverseBooks"."TimelineDateId" IS NULL)
                           OR prior."TimelineDateId" = "UniverseBooks"."TimelineDateId")
                      AND prior."Id" < "UniverseBooks"."Id"
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the blank slots this dropped carried no information, and the schema
            // rollback in AddTimelineDateAssignment discards the ordering anyway.
        }
    }
}
