using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.Sql.Migrations
{
    /// <summary>
    /// Moves existing timeline data onto the new scheme, where dates are created deliberately and
    /// books are assigned to them. Kept separate from AddTimelineDateAssignment so every provider
    /// applies the same two steps in the same order — SQLite needs the split, the rest follow suit.
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
                UPDATE [app].[UniverseBooks]
                SET [TimelineDateId] = NULL
                WHERE [TimelineDateId] IN (
                    SELECT [Id] FROM [app].[TimelineDates] WHERE LTRIM(RTRIM([Date])) = ''
                );
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM [app].[TimelineDates] WHERE LTRIM(RTRIM([Date])) = '';
                """);

            // Ordering used to come from the slot, one book per slot. Books sharing a date now need
            // a position within it, so number each date's books by the order they were added.
            migrationBuilder.Sql(
                """
                WITH numbered AS (
                    SELECT [Order],
                           ROW_NUMBER() OVER (PARTITION BY [UniverseId], [TimelineDateId] ORDER BY [Id]) - 1 AS [NewOrder]
                    FROM [app].[UniverseBooks]
                )
                UPDATE numbered SET [Order] = [NewOrder];
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
