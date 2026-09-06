using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sqlite.Migrations
{
    /// <summary>
    /// Numeric dates no longer keep a leftover free-text name. Separate from
    /// <c>AddUniverseTimelineType</c> because SQLite rebuilds <c>TimelineDates</c> at the end of
    /// that migration, so these updates would otherwise hit the old NOT NULL <c>Date</c> column.
    /// </summary>
    public partial class ClearNumericTimelineDateNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "TimelineDates"
                SET "Name" = NULL
                WHERE "Name" IS NOT NULL
                  AND TRIM("Name") <> ''
                  AND ("YearFrom" IS NOT NULL OR "YearTo" IS NOT NULL);
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Universes"
                SET "TimelineType" = 1
                WHERE "Id" IN (
                    SELECT DISTINCT "UniverseId" FROM "TimelineDates"
                    WHERE "YearFrom" IS NOT NULL OR "YearTo" IS NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Original names can't be reconstructed; an empty string is enough for the following
            // migration's Down to make Name required again.
            migrationBuilder.Sql(
                """
                UPDATE "TimelineDates"
                SET "Name" = COALESCE("Name", '')
                WHERE "Name" IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Universes"
                SET "TimelineType" = 0
                WHERE "Id" IN (
                    SELECT DISTINCT "UniverseId" FROM "TimelineDates"
                    WHERE "YearFrom" IS NOT NULL OR "YearTo" IS NOT NULL);
                """);
        }
    }
}
