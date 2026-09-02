namespace Shelfwarden.Components.Shared;

/// <summary>
/// The presentation half of a universe timeline. Editing lives on the universe page behind an
/// "Edit timeline" toggle so this view can stay a clean poster rather than a form.
/// <para>
/// Rendered as a matrix: one column per timeline date (plus a trailing "Unscheduled" column while
/// anything still needs placing) and one row per series (plus a shared "Standalone" lane). Each
/// lane draws a colourful spanning bar across the dates it occupies, with that date's books as a
/// horizontal strip underneath — so covers stay under their series instead of pooling into one
/// strip, without stacking vertically inside a cell.
/// </para>
/// </summary>
public partial class UniverseTimeline : ComponentBase
{
    private const string NoSeriesColour = "hsl(210 8% 55%)";

    /// <summary>The timeline's columns, in order. Every row's cells line up one-to-one with these.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<UniverseTimelineGroupDto> Groups { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<UniverseTimelineRowDto> Rows { get; set; } = [];

    private static string RowColour(UniverseTimelineRowDto row) =>
        row.SeriesId is null ? NoSeriesColour : ColourFor(row.Label);

    /// <summary>
    /// Inclusive range of columns that carry at least one book for this lane. Used to draw the
    /// colourful spanning bar the way the old Gantt view did — empty columns outside the range
    /// stay a faint rail, empty columns inside stay coloured so the bar reads as continuous.
    /// </summary>
    private static (int Start, int End) OccupiedRange(UniverseTimelineRowDto row)
    {
        int start = -1;
        int end = -1;

        for (int i = 0; i < row.Cells.Count; i++)
        {
            if (row.Cells[i].Entries.Count == 0)
            {
                continue;
            }

            if (start < 0)
            {
                start = i;
            }

            end = i;
        }

        return (start, end);
    }

    /// <summary>
    /// Derives a stable colour from the series name so the same series keeps its colour across
    /// reloads without needing a stored palette. Books with no series share one neutral colour.
    /// </summary>
    private static string ColourFor(string? seriesName)
    {
        if (string.IsNullOrWhiteSpace(seriesName))
        {
            return NoSeriesColour;
        }

        int hash = 17;
        foreach (char c in seriesName)
        {
            hash = (hash * 31) + c;
        }

        int hue = Math.Abs(hash) % 360;
        return $"hsl({hue} 65% 55%)";
    }
}
