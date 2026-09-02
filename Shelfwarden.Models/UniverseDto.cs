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
    IReadOnlyList<UniverseTimelineEntryDto> Timeline,
    IReadOnlyList<UniverseReadingListDto> ReadingLists);

/// <summary>A series set in the universe. Books come from the series itself, not the universe.</summary>
public record UniverseSeriesDto(
    int Id,
    string Name,
    int BookCount,
    IReadOnlyList<SeriesCoverDto> Covers);

/// <summary>
/// One book on the universe timeline. <paramref name="Id"/> is the UniverseBook id, which is what
/// reorder calls send back. <paramref name="TimelineDate"/> is descriptive only — the ordering
/// comes from <paramref name="TimelineOrder"/>.
/// </summary>
public record UniverseTimelineEntryDto(
    int Id,
    int TimelineOrder,
    string? TimelineDate,
    BookListItemDto Book);

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
    string? TimelineDate,
    int TimelineOrder);

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
