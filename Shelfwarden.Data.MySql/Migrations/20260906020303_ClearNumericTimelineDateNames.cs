using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.MySql.Migrations
{
    /// <inheritdoc />
    public partial class ClearNumericTimelineDateNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE TimelineDates
                SET Name = NULL
                WHERE Name IS NOT NULL
                  AND TRIM(Name) <> ''
                  AND (YearFrom IS NOT NULL OR YearTo IS NOT NULL);
                """);

            migrationBuilder.Sql(
                """
                UPDATE Universes u
                INNER JOIN (
                    SELECT DISTINCT UniverseId
                    FROM TimelineDates
                    WHERE YearFrom IS NOT NULL OR YearTo IS NOT NULL
                ) t ON u.Id = t.UniverseId
                SET u.TimelineType = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Original names can't be reconstructed; an empty string is enough for the following
            // migration's Down to make Name required again.
            migrationBuilder.Sql(
                """
                UPDATE TimelineDates
                SET Name = COALESCE(Name, '')
                WHERE Name IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE Universes u
                INNER JOIN (
                    SELECT DISTINCT UniverseId
                    FROM TimelineDates
                    WHERE YearFrom IS NOT NULL OR YearTo IS NOT NULL
                ) t ON u.Id = t.UniverseId
                SET u.TimelineType = 0;
                """);
        }
    }
}
