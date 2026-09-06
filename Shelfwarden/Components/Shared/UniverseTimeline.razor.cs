namespace Shelfwarden.Components.Shared;

/// <summary>
/// The presentation half of a universe timeline. Editing lives on the universe page behind an
/// "Edit timeline" toggle so this view can stay a clean poster rather than a form.
/// <para>
/// Rendered as one lane per series (plus a shared "Standalone" lane). Clicking a lane's own name
/// jumps to its series page (or, for the shared lane, opens a covers modal — there's no
/// "Standalone" page to go to); clicking one of its bars opens a covers modal for just the books
/// on that bar, each linking to its book details page.
/// </para>
/// <para>
/// When <see cref="TimelineType"/> is <see cref="Enums.TimelineType.Numeric"/>, bars sit on a real
/// proportional axis built from each date's <c>YearFrom</c>/<c>YearTo</c> — <see cref="Rows"/>
/// arrives with overlapping/touching dates on the same lane already merged into one bar (e.g. two
/// dates spanning 2005-2008 and 2007-2012 become a single "2005-2012" bar) — see
/// <c>UniverseService.BuildSegments</c>. Otherwise bars fall back to evenly-spaced columns, one
/// per date, in hand-picked order.
/// </para>
/// </summary>
public partial class UniverseTimeline : ComponentBase
{
    private const string NoSeriesColour = "hsl(210 8% 55%)";

    /// <summary>Fixed width for a date column in grid mode (and the pill floor in axis mode).</summary>
    private const double GridColumnWidthRem = 7.5;

    /// <summary>Sticky lane-label column width, shared by both rendering modes.</summary>
    private const double LaneLabelWidthRem = 11;

    /// <summary>Baseline pixels-per-year before the min/max clamps kick in.</summary>
    private const double DesiredRemPerYear = 0.6;

    /// <summary>Floor for the numeric axis's total width, so short ranges aren't cramped.</summary>
    private const double MinAxisWidthRem = 40;

    /// <summary>Ceiling for the numeric axis's total width, so century-spanning sagas stay scrollable rather than absurd.</summary>
    private const double MaxAxisWidthRem = 320;

    /// <summary>Minimum pill width on the numeric axis, so a point-in-time date is still visible.</summary>
    private const double MinPillWidthRem = 3.5;

    /// <summary>Width of a non-numeric date's slot trailing the numeric axis (e.g. "Unscheduled").</summary>
    private const double TrailingSlotWidthRem = 9;

    /// <summary>
    /// Gap between trailing slots, and between the axis and the first trailing slot. Wide enough
    /// that the last tick's year label (drawn to the right of its line) never runs into it.
    /// </summary>
    private const double TrailingGapRem = 3;

    /// <summary>Roughly how many tick marks to draw across the numeric axis.</summary>
    private const int TargetTickCount = 8;

    /// <summary>The timeline's columns, in order. Every row's cells line up one-to-one with these.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<UniverseTimelineGroupDto> Groups { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<UniverseTimelineRowDto> Rows { get; set; } = [];

    /// <summary>Named renders evenly-spaced columns; Numeric renders a proportional axis.</summary>
    [Parameter]
    public TimelineType TimelineType { get; set; }

    private bool IsNumeric => TimelineType == TimelineType.Numeric;

    private string? coversModalTitle;
    private string coversModalColour = NoSeriesColour;
    private IReadOnlyList<UniverseTimelineEntryDto> coversModalEntries = [];

    private List<UniverseTimelineGroupDto> TrailingGroups =>
        Groups.Where(g => !(g.YearFrom.HasValue || g.YearTo.HasValue)).ToList();

    private List<UniverseTimelineGroupDto> NumericGroups =>
        Groups.Where(g => g.YearFrom.HasValue || g.YearTo.HasValue).ToList();

    private int AxisMinYear => NumericGroups
        .Select(g => Math.Min(g.YearFrom ?? g.YearTo!.Value, g.YearTo ?? g.YearFrom!.Value))
        .DefaultIfEmpty(0)
        .Min();

    private int AxisMaxYear => NumericGroups
        .Select(g => Math.Max(g.YearFrom ?? g.YearTo!.Value, g.YearTo ?? g.YearFrom!.Value))
        .DefaultIfEmpty(0)
        .Max();

    private double AxisSpanYears => Math.Max(1, AxisMaxYear - AxisMinYear);

    private double AxisWidthRem => Math.Clamp(AxisSpanYears * DesiredRemPerYear, MinAxisWidthRem, MaxAxisWidthRem);

    private double RemPerYear => AxisWidthRem / AxisSpanYears;

    // TimelineDateId is nullable (the "Unscheduled" trailing group has none), so a plain int? key
    // in a Dictionary trips the notnull constraint warning — a sentinel key sidesteps that.
    private const int UnscheduledKey = int.MinValue;

    private Dictionary<int, int> TrailingIndexByDateId =>
        TrailingGroups.Select((g, i) => (Key: g.TimelineDateId ?? UnscheduledKey, i)).ToDictionary(x => x.Key, x => x.i);

    /// <summary>Total scrollable width of the numeric-mode chart, axis plus any trailing slots.</summary>
    private double TotalAxisWidthRem
    {
        get
        {
            double width = AxisWidthRem;
            if (TrailingGroups.Count > 0)
            {
                width += TrailingGapRem + (TrailingGroups.Count * TrailingSlotWidthRem)
                    + (Math.Max(0, TrailingGroups.Count - 1) * TrailingGapRem);
            }

            return width;
        }
    }

    private double TrailingLeftRem(int trailingIndex) =>
        AxisWidthRem + TrailingGapRem + (trailingIndex * (TrailingSlotWidthRem + TrailingGapRem));

    /// <summary>
    /// Explicit <c>grid-template-columns</c> track list for the non-numeric fallback — every date
    /// column is the same fixed width now that books no longer render inline.
    /// </summary>
    private string GridTemplateColumns => Groups.Count == 0
        ? $"{LaneLabelWidthRem}rem"
        : $"{LaneLabelWidthRem}rem " + string.Join(' ', Enumerable.Repeat($"{GridColumnWidthRem}rem", Groups.Count));

    private static string RowColour(UniverseTimelineRowDto row) =>
        row.SeriesId is null ? NoSeriesColour : ColourFor(row.Label);

    private static IReadOnlyList<UniverseTimelineEntryDto> AllEntries(UniverseTimelineRowDto row) =>
        row.Cells.SelectMany(c => c.Entries).ToList();

    private void OpenLaneCovers(UniverseTimelineRowDto row) =>
        OpenCoversModal(row.Label, RowColour(row), AllEntries(row));

    private void OpenSegmentCovers(UniverseTimelineRowDto row, UniverseTimelineSegmentDto segment) =>
        OpenCoversModal($"{row.Label} · {segment.Label}", RowColour(row), segment.Entries);

    private void OpenCellCovers(UniverseTimelineRowDto row, UniverseTimelineGroupDto group, UniverseTimelineCellDto cell) =>
        OpenCoversModal($"{row.Label} · {group.Label}", RowColour(row), cell.Entries);

    private void OpenCoversModal(string title, string colour, IReadOnlyList<UniverseTimelineEntryDto> entries)
    {
        coversModalTitle = title;
        coversModalColour = colour;
        coversModalEntries = entries;
    }

    private void CloseCoversModal()
    {
        coversModalTitle = null;
        coversModalEntries = [];
    }

    /// <summary>
    /// Inclusive range of columns that carry at least one book for this lane. Used in grid mode to
    /// decide which cells draw a colourful bar pill versus a quiet rail.
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

    /// <summary>Geometry for one of a lane's pre-merged numeric segments, positioned on the shared axis.</summary>
    private (double LeftRem, double WidthRem) SegmentGeometry(UniverseTimelineSegmentDto segment)
    {
        double leftRem = (segment.YearFrom - AxisMinYear) * RemPerYear;
        double widthRem = Math.Max(MinPillWidthRem, (segment.YearTo - segment.YearFrom) * RemPerYear);
        return (leftRem, widthRem);
    }

    /// <summary>A lane's trailing (non-numeric, or unscheduled) pills — same slot for every lane.</summary>
    private IEnumerable<(UniverseTimelineGroupDto Group, UniverseTimelineCellDto Cell, double LeftRem)> TrailingCells(
        UniverseTimelineRowDto row)
    {
        var trailingIndex = TrailingIndexByDateId;
        int count = Math.Min(Groups.Count, row.Cells.Count);

        for (int i = 0; i < count; i++)
        {
            var group = Groups[i];
            if (group.YearFrom.HasValue || group.YearTo.HasValue)
            {
                continue;
            }

            var cell = row.Cells[i];
            if (cell.Entries.Count == 0)
            {
                continue;
            }

            int idx = trailingIndex.GetValueOrDefault(group.TimelineDateId ?? UnscheduledKey);
            yield return (group, cell, TrailingLeftRem(idx));
        }
    }

    /// <summary>Tick marks (and year labels) drawn across the top of the numeric axis.</summary>
    private IEnumerable<Tick> AxisTicks()
    {
        int step = NiceStep(AxisSpanYears / TargetTickCount);
        int start = (int)(Math.Floor((double)AxisMinYear / step) * step);

        for (int year = start; year <= AxisMaxYear; year += step)
        {
            if (year < AxisMinYear)
            {
                continue;
            }

            yield return new Tick((year - AxisMinYear) * RemPerYear, year.ToString());
        }
    }

    /// <summary>Rounds a rough tick step up to a "nice" 1/2/5×10^n number.</summary>
    private static int NiceStep(double roughStep)
    {
        roughStep = Math.Max(roughStep, 1);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(roughStep)));
        double residual = roughStep / magnitude;
        double niceResidual = residual <= 1 ? 1 : residual <= 2 ? 2 : residual <= 5 ? 5 : 10;
        return Math.Max(1, (int)(niceResidual * magnitude));
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

    private readonly record struct Tick(double LeftRem, string Label);
}
