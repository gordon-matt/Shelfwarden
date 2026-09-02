namespace Shelfwarden.Models;

/// <summary>
/// A fictional universe grouping related series and books. Catalog data like
/// <see cref="SeriesDto"/> — everyone can browse them, administrators maintain them.
/// </summary>
public record UniverseDto(
    int Id,
    string Name,
    string? Description,
    int SeriesCount,
    int BookCount,
    int ReadingListCount,
    DateTime CreatedAt,
    IReadOnlyList<SeriesCoverDto> Covers);

/// <summary>Minimal shape for universe dropdowns on the book / series edit forms.</summary>
public record UniverseOptionDto(int Id, string Name);

/// <summary>Universe with everything the detail page's tabs need, in one round-trip.</summary>
public record UniverseDetailDto(
    int Id,
    string Name,
    string? Description,
    DateTime CreatedAt,
    IReadOnlyList<UniverseSeriesDto> Series,
    UniverseTimelineDto Timeline,
    IReadOnlyList<UniverseReadingListDto> ReadingLists);

/// <summary>A series set in the universe. Books come from the series itself, not the universe.</summary>
public record UniverseSeriesDto(
    int Id,
    string Name,
    int BookCount,
    IReadOnlyList<SeriesCoverDto> Covers);

/// <summary>
/// A date on a universe's timeline: the thing books are assigned to, and the thing administrators
/// create, rename, reorder and delete. <paramref name="Order"/> is its left-to-right position.
/// </summary>
public record UniverseTimelineDateDto(int Id, string Date, int Order, int BookCount);

/// <summary>
/// One book on the universe timeline. <paramref name="Id"/> is the UniverseBook id, which is what
/// reorder calls send back. <paramref name="TimelineDateId"/> is the date it's assigned to, or
/// <c>null</c> while it's unscheduled. <paramref name="Order"/> is its position among the books
/// sharing that same date.
/// </summary>
public record UniverseTimelineEntryDto(
    int Id,
    int? TimelineDateId,
    int Order,
    string? TimelineDate,
    BookListItemDto Book);

/// <summary>
/// One column of the timeline: a date and the books sitting on it, in their within-date order.
/// A <paramref name="TimelineDateId"/> of <c>null</c> is the trailing "Unscheduled" column, which
/// is only present when some book in the universe has no date yet.
/// </summary>
public record UniverseTimelineGroupDto(
    int? TimelineDateId,
    string Label,
    IReadOnlyList<UniverseTimelineEntryDto> Entries);

/// <summary>One lane's books at one date. Empty when that series has nothing at that date.</summary>
public record UniverseTimelineCellDto(
    int? TimelineDateId,
    IReadOnlyList<UniverseTimelineEntryDto> Entries);

/// <summary>
/// One lane of the timeline grid: a series, or the shared "Standalone" bucket for books with no
/// series. <paramref name="Cells"/> lines up one-to-one with <see cref="UniverseTimelineDto.Groups"/>
/// so the view can render the lane as a row of a matrix without re-grouping anything.
/// </summary>
public record UniverseTimelineRowDto(
    int? SeriesId,
    string Label,
    IReadOnlyList<UniverseTimelineCellDto> Cells);

/// <summary>
/// The universe timeline in the shapes the UI needs. <paramref name="Dates"/> drives the date
/// dropdowns and the date-management list; <paramref name="Groups"/> is the date-by-date view the
/// editor works in; <paramref name="Rows"/> crosses those groups with series lanes for the
/// read-only grid; <paramref name="Entries"/> is the flat list, for counts.
/// </summary>
public record UniverseTimelineDto(
    IReadOnlyList<UniverseTimelineDateDto> Dates,
    IReadOnlyList<UniverseTimelineGroupDto> Groups,
    IReadOnlyList<UniverseTimelineRowDto> Rows,
    IReadOnlyList<UniverseTimelineEntryDto> Entries);

/// <summary>A reading order belonging to the universe (publication, chronological, …).</summary>
public record UniverseReadingListDto(
    int Id,
    string Name,
    string? Description,
    int BookCount);

/// <summary>A book's membership of a universe, as shown on the book edit page.</summary>
public record UniverseMembershipDto(
    int UniverseId,
    string UniverseName,
    int? TimelineDateId,
    string? TimelineDate);

public record CreateTimelineDateRequest
{
    [Required, StringLength(50)]
    public required string Date { get; init; }
}

public record CreateUniverseRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    public string? Description { get; init; }
}

public record UpdateUniverseRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    public string? Description { get; init; }
}
