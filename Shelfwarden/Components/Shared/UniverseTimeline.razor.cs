namespace Shelfwarden.Components.Shared;

/// <summary>
/// The presentation half of a universe timeline. Editing lives on the universe page behind an
/// "Edit timeline" toggle so this view can stay a clean poster rather than a form.
/// </summary>
public partial class UniverseTimeline : ComponentBase
{
    private const string NoSeriesColour = "hsl(210 8% 55%)";

    [Parameter, EditorRequired]
    public IReadOnlyList<UniverseTimelineEntryDto> Entries { get; set; } = [];

    /// <summary>Series present on the timeline with the colour used for their entries.</summary>
    private List<(string Name, string Colour)> Legend => Entries
        .Select(e => e.Book.SeriesName)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct()
        .Select(name => (Name: name!, Colour: ColourFor(name)))
        .ToList();

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
