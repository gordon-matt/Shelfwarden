namespace Shelfwarden.Models;

public record AuthorDto(int Id, string Name, string? Biography);

/// <summary>Minimal author reference (id + name) used for pseudonym links on the detail page.</summary>
public record AuthorRefDto(int Id, string Name);

/// <summary>External link shown on the author detail page (Wikipedia, homepage, etc.).</summary>
public record AuthorLinkDto(int Id, string? Name, string Url);

/// <summary>List-view projection used on the authors listing page.</summary>
public record AuthorListItemDto(
    int Id,
    string Name,
    string? Biography,
    int BookCount);

/// <summary>
/// A candidate author match returned by one of the pluggable author metadata providers
/// (OpenLibrary, Wikidata, Goodreads, …). Carries the <em>full</em> biography so an import doesn't
/// need a second round-trip; the UI shows <see cref="BioPreview"/> instead.
/// </summary>
public record ExternalAuthorMatchDto(
    string Provider,
    string ProviderId,
    string Name,
    string? BirthDate,
    string? DeathDate,
    string? Biography,
    string? PhotoUrl,
    string? TopBooks,
    string? InfoUrl)
{
    public bool HasBio => !string.IsNullOrWhiteSpace(Biography);

    public bool HasPhoto => !string.IsNullOrWhiteSpace(PhotoUrl);

    /// <summary>First ~180 chars of the biography for the candidate list.</summary>
    public string? BioPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Biography))
            {
                return null;
            }

            string trimmed = Biography.Trim();
            const int max = 180;
            return trimmed.Length <= max ? trimmed : $"{trimmed[..max]}...";
        }
    }
}

/// <summary>
/// Which candidate to pull each piece of metadata from. The biography and photo may come from
/// different providers; either may be null to leave that piece untouched.
/// </summary>
public record AuthorMetadataImportRequest(
    ExternalAuthorMatchDto? BiographyMatch,
    ExternalAuthorMatchDto? PhotoMatch);

/// <summary>Outcome of importing external author metadata into Shelfwarden.</summary>
public record AuthorMetadataImportResultDto(
    int AuthorId,
    string Provider,
    string ProviderId,
    bool BiographyUpdated,
    bool PhotoUpdated,
    bool LinkAdded);

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
    IReadOnlyList<BookListItemDto> StandaloneBooks,
    AuthorRefDto? PrimaryAuthor,
    IReadOnlyList<AuthorRefDto> Pseudonyms,
    IReadOnlyList<AuthorLinkDto> Links);

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
    IReadOnlyList<SeriesCoverDto> Covers,
    int? UniverseId = null,
    string? UniverseName = null);