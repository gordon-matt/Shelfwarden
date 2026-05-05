namespace Shelfwarden.Models;

public record AuthorDto(int Id, string Name, string? Biography);

/// <summary>List-view projection used on the authors listing page.</summary>
public record AuthorListItemDto(
    int Id,
    string Name,
    string? Biography,
    int BookCount);

/// <summary>Candidate author match returned by OpenLibrary search.</summary>
public record OpenLibraryAuthorMatchDto(
    string OpenLibraryId,
    string Name,
    string? BirthDate,
    string? DeathDate,
    bool HasBio,
    bool HasPhoto,
    string? BioPreview);

/// <summary>Outcome of importing OpenLibrary author metadata into Shelfwarden.</summary>
public record AuthorOpenLibraryImportResultDto(
    int AuthorId,
    string OpenLibraryId,
    bool BiographyUpdated,
    bool PhotoUpdated);

/// <summary>Outcome of manually updating author biography and/or photo.</summary>
public record AuthorProfileUpdateResultDto(
    int AuthorId,
    string? Biography,
    bool BiographyUpdated,
    bool PhotoUpdated);

/// <summary>
/// Detailed author projection used on <c>/authors/{id}</c>. Pre-buckets the author's books into
/// <see cref="SeriesGroups"/> (one entry per series the author has any book in) and
/// <see cref="StandaloneBooks"/> (books not assigned to a series). Each series group also carries
/// the cover thumbnails of the first few books so the UI can render a collage without making
/// further round trips.
/// </summary>
public record AuthorDetailDto(
    int Id,
    string Name,
    string? Biography,
    int BookCount,
    IReadOnlyList<AuthorSeriesGroupDto> SeriesGroups,
    IReadOnlyList<BookListItemDto> StandaloneBooks);

/// <summary>
/// One series an author has books in, together with the covers used by the collage and a
/// jump target for the full series page.
/// </summary>
public record AuthorSeriesGroupDto(
    int SeriesId,
    string SeriesName,
    int BookCount,
    IReadOnlyList<SeriesCoverDto> Covers);

/// <summary>Tuple of (book id, cover path) used to build series collages without a second fetch.</summary>
public record SeriesCoverDto(int BookId, string? CoverImagePath);

/// <summary>
/// All series a user can browse. Mirrors <see cref="AuthorListItemDto"/> in shape — name +
/// counts + a few covers — so the listing page can be built from one query.
/// </summary>
public record SeriesListItemDto(
    int Id,
    string Name,
    string? Description,
    int BookCount,
    IReadOnlyList<SeriesCoverDto> Covers);