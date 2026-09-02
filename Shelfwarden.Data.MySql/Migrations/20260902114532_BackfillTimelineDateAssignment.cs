using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfwarden.Data.MySql.Migrations
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
            // isn't a date anyone chose, so those books go back to being unscheduled. MySQL won't
            // let an UPDATE's subquery read the table being updated, hence the JOIN form.
            migrationBuilder.Sql(
                """
                UPDATE UniverseBooks ub
                JOIN TimelineDates td ON td.Id = ub.TimelineDateId
                SET ub.TimelineDateId = NULL
                WHERE TRIM(td.Date) = '';
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM TimelineDates WHERE TRIM(Date) = '';
                """);

            // Ordering used to come from the slot, one book per slot. Books sharing a date now need
            // a position within it, so number each date's books by the order they were added. The
            // derived table is materialised, which is what makes reading UniverseBooks here legal.
            migrationBuilder.Sql(
                """
                UPDATE UniverseBooks ub
                JOIN (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY UniverseId, TimelineDateId ORDER BY Id) - 1 AS NewOrder
                    FROM UniverseBooks
                ) numbered ON numbered.Id = ub.Id
                SET ub.`Order` = numbered.NewOrder;
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
