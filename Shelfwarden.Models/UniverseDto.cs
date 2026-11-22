namespace Shelfwarden.Models;

/// <summary>
/// A fictional universe grouping related series and books. Catalog data like
/// <see cref="SeriesDto"/> — everyone can browse them, administrators maintain them.
/// </summary>
/// <param name="Id">Universe id.</param>
/// <param name="Name">Universe name.</param>
/// <param name="Description">Description, or <see langword="null"/>.</param>
/// <param name="SeriesCount">Series assigned to the universe.</param>
/// <param name="BookCount">Books on the timeline.</param>
/// <param name="ReadingListCount">Reading orders that belong to the universe.</param>
/// <param name="CreatedAt">When it was created, in UTC.</param>
/// <param name="Covers">Up to four covers for the list tile.</param>
/// <param name="TimelineType">Named labels, or numeric years once any date has a year.</param>
public record UniverseDto(
    int Id,
    string Name,
    string? Description,
    int SeriesCount,
    int BookCount,
    int ReadingListCount,
    DateTime CreatedAt,
    IReadOnlyList<SeriesCoverDto> Covers,
    TimelineType TimelineType);

/// <summary>Minimal shape for universe dropdowns on the book / series edit forms.</summary>
/// <param name="Id">Universe id.</param>
/// <param name="Name">Universe name.</param>
/// <param name="TimelineType">Named labels, or numeric years.</param>
public record UniverseOptionDto(int Id, string Name, TimelineType TimelineType);

/// <summary>Universe with everything the detail page's tabs need, in one round-trip.</summary>
/// <param name="Id">Universe id.</param>
/// <param name="Name">Universe name.</param>
/// <param name="Description">Description, or <see langword="null"/>.</param>
/// <param name="CreatedAt">When it was created, in UTC.</param>
/// <param name="TimelineType">Named labels, or numeric years.</param>
/// <param name="Series">Series assigned to the universe.</param>
/// <param name="Timeline">Dates, groups, lanes, and the flat book list.</param>
/// <param name="ReadingLists">Reading orders that belong to the universe.</param>
public record UniverseDetailDto(
    int Id,
    string Name,
    string? Description,
    DateTime CreatedAt,
    TimelineType TimelineType,
    IReadOnlyList<UniverseSeriesDto> Series,
    UniverseTimelineDto Timeline,
    IReadOnlyList<UniverseReadingListDto> ReadingLists);

/// <summary>A series set in the universe. Books come from the series itself, not the universe.</summary>
/// <param name="Id">Series id.</param>
/// <param name="Name">Series name.</param>
/// <param name="BookCount">Books in the series.</param>
/// <param name="Covers">Up to four covers.</param>
public record UniverseSeriesDto(
    int Id,
    string Name,
    int BookCount,
    IReadOnlyList<SeriesCoverDto> Covers);

/// <summary>
/// A date on a universe's timeline: the thing books are assigned to, and the thing administrators
/// create, rename, reorder and delete. <paramref name="Order"/> is its left-to-right position when
/// no date in the universe has a numeric year; once one does, <paramref name="YearFrom"/> /
/// <paramref name="YearTo"/> take over and <paramref name="Order"/> only breaks ties.
/// </summary>
/// <param name="Id">Timeline date id.</param>
/// <param name="Name">Label, or <see langword="null"/> when the date is only a year range.</param>
/// <param name="Order">Left-to-right position while no date in the universe has a year.</param>
/// <param name="YearFrom">First year, or <see langword="null"/> for a named date.</param>
/// <param name="YearTo">Last year, or <see langword="null"/> when the date is a single year or a name.</param>
/// <param name="BookCount">Books assigned to this date.</param>
public record UniverseTimelineDateDto(
    int Id,
    string? Name,
    int Order,
    int? YearFrom,
    int? YearTo,
    int BookCount)
{
    /// <summary>
    /// What dropdowns and the editor show: the named label, or a year-range derived from
    /// <see cref="YearFrom"/>/<see cref="YearTo"/> when this date is numeric.
    /// </summary>
    public string Label => !string.IsNullOrWhiteSpace(Name)
        ? Name
        : YearFrom is int from && YearTo is int to && from != to
            ? $"{from}-{to}"
            : (YearFrom ?? YearTo)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
}

/// <summary>
/// One book on the universe timeline. <paramref name="Id"/> is the UniverseBook id, which is what
/// reorder calls send back. <paramref name="TimelineDateId"/> is the date it's assigned to, or
/// <c>null</c> while it's unscheduled. <paramref name="Order"/> is its position among the books
/// sharing that same date.
/// </summary>
/// <param name="Id">Universe-book id. Reorder calls send this back.</param>
/// <param name="TimelineDateId">Date the book is assigned to, or <see langword="null"/> while it is unscheduled.</param>
/// <param name="Order">Position among the books that share that date.</param>
/// <param name="TimelineDate">Label of that date, or <see langword="null"/> while unscheduled.</param>
/// <param name="Book">The book.</param>
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
/// <param name="TimelineDateId">Date id, or <see langword="null"/> for the unscheduled column.</param>
/// <param name="Label">Column heading.</param>
/// <param name="YearFrom">First year, or <see langword="null"/> for a named or unscheduled column.</param>
/// <param name="YearTo">Last year, or <see langword="null"/> when the column is a single year, a name, or unscheduled.</param>
/// <param name="Entries">Books in this column, in their within-date order.</param>
public record UniverseTimelineGroupDto(
    int? TimelineDateId,
    string Label,
    int? YearFrom,
    int? YearTo,
    IReadOnlyList<UniverseTimelineEntryDto> Entries);

/// <summary>One lane's books at one date. Empty when that series has nothing at that date.</summary>
/// <param name="TimelineDateId">Date id, or <see langword="null"/> for the unscheduled column.</param>
/// <param name="Entries">Books in this cell. Empty when the lane has nothing at this date.</param>
public record UniverseTimelineCellDto(
    int? TimelineDateId,
    IReadOnlyList<UniverseTimelineEntryDto> Entries);

/// <summary>
/// One merged bar on a lane's numeric axis. Adjacent/overlapping <see cref="TimelineDate"/> ranges
/// the lane occupies are combined into a single segment so, e.g., dates "2005-2008" and "2007-2012"
/// on the same lane become one "2005-2012" bar instead of two overlapping ones.
/// </summary>
/// <param name="Label">Text drawn on the bar.</param>
/// <param name="YearFrom">First year of the merged range.</param>
/// <param name="YearTo">Last year of the merged range.</param>
/// <param name="Entries">Books that fell inside the merged range.</param>
public record UniverseTimelineSegmentDto(
    string Label,
    int YearFrom,
    int YearTo,
    IReadOnlyList<UniverseTimelineEntryDto> Entries);

/// <summary>
/// One lane of the timeline grid: a series, or the shared "Standalone" bucket for books with no
/// series. <paramref name="Cells"/> lines up one-to-one with <see cref="UniverseTimelineDto.Groups"/>
/// so the view can render the lane as a row of a matrix without re-grouping anything.
/// <paramref name="Segments"/> is the same data pre-merged for numeric-axis rendering — see
/// <see cref="UniverseTimelineSegmentDto"/>.
/// </summary>
/// <param name="SeriesId">Series id, or <see langword="null"/> for the standalone lane.</param>
/// <param name="Label">Series name, or the standalone heading.</param>
/// <param name="Cells">One cell per <see cref="UniverseTimelineDto.Groups"/> entry, in the same order.</param>
/// <param name="Segments">Those cells merged for a numeric axis.</param>
public record UniverseTimelineRowDto(
    int? SeriesId,
    string Label,
    IReadOnlyList<UniverseTimelineCellDto> Cells,
    IReadOnlyList<UniverseTimelineSegmentDto> Segments);

/// <summary>
/// The universe timeline in the shapes the UI needs. <paramref name="Dates"/> drives the date
/// dropdowns and the date-management list; <paramref name="Groups"/> is the date-by-date view the
/// editor works in; <paramref name="Rows"/> crosses those groups with series lanes for the
/// read-only grid; <paramref name="Entries"/> is the flat list, for counts.
/// </summary>
/// <param name="Dates">Dates for the dropdowns and the date-management list.</param>
/// <param name="Groups">Date-by-date columns the editor works in.</param>
/// <param name="Rows">Those columns crossed with series lanes.</param>
/// <param name="Entries">Every book on the timeline, including unscheduled ones.</param>
public record UniverseTimelineDto(
    IReadOnlyList<UniverseTimelineDateDto> Dates,
    IReadOnlyList<UniverseTimelineGroupDto> Groups,
    IReadOnlyList<UniverseTimelineRowDto> Rows,
    IReadOnlyList<UniverseTimelineEntryDto> Entries);

/// <summary>A reading order belonging to the universe (publication, chronological, …).</summary>
/// <param name="Id">Reading list id.</param>
/// <param name="Name">Reading order name.</param>
/// <param name="Description">Description, or <see langword="null"/>.</param>
/// <param name="BookCount">Books in the reading order.</param>
public record UniverseReadingListDto(
    int Id,
    string Name,
    string? Description,
    int BookCount);

/// <summary>A book's membership of a universe, as shown on the book edit page.</summary>
/// <param name="UniverseId">Universe id.</param>
/// <param name="UniverseName">Universe name.</param>
/// <param name="TimelineDateId">Date the book is assigned to, or <see langword="null"/> while unscheduled.</param>
/// <param name="TimelineDate">That date's label, or <see langword="null"/> while unscheduled.</param>
public record UniverseMembershipDto(
    int UniverseId,
    string UniverseName,
    int? TimelineDateId,
    string? TimelineDate);

public record CreateTimelineDateRequest
{
    [StringLength(50)]
    public string? Name { get; init; }
}

/// <summary>
/// A universe to create. The name is matched without case, and a leading "A", "An", or "The" does not make it a different name.
/// A blank description is stored as <see langword="null"/>. The new universe uses a named timeline.
/// </summary>
public record CreateUniverseRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    public string? Description { get; init; }
}

/// <summary>
/// A new name and description. Matching ignores case and a leading "A", "An", or "The".
/// A blank description is cleared.
/// </summary>
public record UpdateUniverseRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    public string? Description { get; init; }
}