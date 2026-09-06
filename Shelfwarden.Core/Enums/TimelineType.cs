namespace Shelfwarden.Enums;

/// <summary>
/// How a universe's timeline dates are interpreted for display and ordering.
/// </summary>
public enum TimelineType
{
    /// <summary>Dates are free text (e.g. "10,191 AG"), hand-ordered, and shown as evenly-spaced columns.</summary>
    Named = 0,

    /// <summary>
    /// Dates carry a numeric <c>YearFrom</c>/<c>YearTo</c>, so the timeline renders on a real
    /// proportional axis and overlapping ranges are merged into a single label per lane.
    /// </summary>
    Numeric = 1,
}
