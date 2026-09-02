namespace Shelfwarden.Components.Shared;

/// <summary>
/// The presentation half of a universe timeline. Editing lives on the universe page behind an
/// "Edit timeline" toggle so this view can stay a clean poster rather than a form.
/// <para>
/// Rendered as a matrix: one column per timeline date (plus a trailing "Unscheduled" column while
/// anything still needs placing) and one row per series (plus a shared "Standalone" lane). Column
/// widths are derived from the busiest cell in each date so headers stretch over their books and
/// adjacent dates can't overlap; the outer scroll is the only horizontal scrollbar.
/// </para>
/// </summary>
public partial class UniverseTimeline : ComponentBase
{
    private const string NoSeriesColour = "hsl(210 8% 55%)";

    /// <summary>Cover card width used when deriving a column's minimum size.</summary>
    private const double CardWidthRem = 5.25;

    /// <summary>Gap between covers inside a date cell.</summary>
    private const double CardGapRem = 0.5;

    /// <summary>Floor for empty date columns so the faint rail still has something to sit in.</summary>
    private const double EmptyColumnMinRem = 5.5;

    /// <summary>The timeline's columns, in order. Every row's cells line up one-to-one with these.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<UniverseTimelineGroupDto> Groups { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<UniverseTimelineRowDto> Rows { get; set; } = [];

    /// <summary>
    /// Explicit <c>grid-template-columns</c> track list. Computed from content rather than left to
    /// <c>max-content</c>, because a grid item with overflowing flex children (or negative margins
    /// on the spanning bar) can otherwise shrink below its books and spill into the next date.
    /// </summary>
    private string GridTemplateColumns
    {
        get
        {
            if (Groups.Count == 0)
            {
                return "11rem";
            }

            var tracks = new string[Groups.Count];
            for (int c = 0; c < Groups.Count; c++)
            {
                tracks[c] = $"{ColumnMinWidthRem(c).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}rem";
            }

            return "11rem " + string.Join(' ', tracks);
        }
    }

    private double ColumnMinWidthRem(int columnIndex)
    {
        int maxBooks = 0;
        foreach (var row in Rows)
        {
            if (columnIndex < row.Cells.Count)
            {
                maxBooks = Math.Max(maxBooks, row.Cells[columnIndex].Entries.Count);
            }
        }

        if (maxBooks <= 0)
        {
            return EmptyColumnMinRem;
        }

        return (maxBooks * CardWidthRem) + ((maxBooks - 1) * CardGapRem);
    }

    private static string RowColour(UniverseTimelineRowDto row) =>
        row.SeriesId is null ? NoSeriesColour : ColourFor(row.Label);

    /// <summary>
    /// Inclusive range of columns that carry at least one book for this lane. Used to decide which
    /// cells draw a colourful bar pill versus a quiet rail.
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
