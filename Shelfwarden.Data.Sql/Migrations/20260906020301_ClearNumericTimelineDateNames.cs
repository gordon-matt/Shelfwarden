using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sql.Migrations
{
    /// <inheritdoc />
    public partial class ClearNumericTimelineDateNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [app].[TimelineDates]
                SET [Name] = NULL
                WHERE [Name] IS NOT NULL
                  AND LTRIM(RTRIM([Name])) <> ''
                  AND ([YearFrom] IS NOT NULL OR [YearTo] IS NOT NULL);
                """);

            migrationBuilder.Sql(
                """
                UPDATE [app].[Universes]
                SET [TimelineType] = 1
                WHERE [Id] IN (
                    SELECT DISTINCT [UniverseId] FROM [app].[TimelineDates]
                    WHERE [YearFrom] IS NOT NULL OR [YearTo] IS NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Original names can't be reconstructed; an empty string is enough for the following
            // migration's Down to make Name required again.
            migrationBuilder.Sql(
                """
                UPDATE [app].[TimelineDates]
                SET [Name] = ISNULL([Name], N'')
                WHERE [Name] IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE [app].[Universes]
                SET [TimelineType] = 0
                WHERE [Id] IN (
                    SELECT DISTINCT [UniverseId] FROM [app].[TimelineDates]
                    WHERE [YearFrom] IS NOT NULL OR [YearTo] IS NOT NULL);
                """);
        }
    }
}
