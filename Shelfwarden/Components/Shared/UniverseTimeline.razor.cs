namespace Shelfwarden.Components.Shared;

/// <summary>
/// The presentation half of a universe timeline. Editing lives on the universe page behind an
/// "Edit timeline" toggle so this view can stay a clean poster rather than a form.
/// <para>
/// Rendered as a matrix: one column per timeline date (plus a trailing "Unscheduled" column while
/// anything still needs placing) and one row per series (plus a shared "Standalone" lane), so a
/// series' books stay in that series' lane instead of being pooled into one strip.
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
