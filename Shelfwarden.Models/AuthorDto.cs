namespace Shelfwarden.Models;

/// <summary>An author, including the biography when one has been saved.</summary>
/// <param name="Id">Author id.</param>
/// <param name="Name">Author name.</param>
/// <param name="Biography">Biography, or <see langword="null"/> when none is stored.</param>
public record AuthorDto(int Id, string Name, string? Biography);

/// <summary>Minimal author reference (id + name) used for pseudonym links on the detail page.</summary>
/// <param name="Id">Author id.</param>
/// <param name="Name">Author name.</param>
public record AuthorRefDto(int Id, string Name);

/// <summary>External link shown on the author detail page (Wikipedia, homepage, etc.).</summary>
/// <param name="Id">Link id.</param>
/// <param name="Name">Label, or <see langword="null"/> to show the address itself.</param>
/// <param name="Url">Absolute address.</param>
public record AuthorLinkDto(int Id, string? Name, string Url);

/// <summary>List-view projection used on the authors listing page.</summary>
/// <param name="Id">Author id.</param>
/// <param name="Name">Author name.</param>
/// <param name="Biography">Biography, or <see langword="null"/>.</param>
/// <param name="BookCount">Books credited to this author.</param>
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
/// <param name="Provider">Source name, such as Open Library.</param>
/// <param name="ProviderId">That source's id for this person.</param>
/// <param name="Name">Name the source returned.</param>
/// <param name="BirthDate">Birth date as the source wrote it, or <see langword="null"/>.</param>
/// <param name="DeathDate">Death date as the source wrote it, or <see langword="null"/>.</param>
/// <param name="Biography">Full biography, or <see langword="null"/>.</param>
/// <param name="PhotoUrl">Photo address, or <see langword="null"/>.</param>
/// <param name="TopBooks">Notable titles, or <see langword="null"/>.</param>
/// <param name="InfoUrl">Page about this person at the source, or <see langword="null"/>.</param>
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
    /// <summary><see langword="true"/> when <see cref="Biography"/> contains something other than whitespace.</summary>
    public bool HasBio => !string.IsNullOrWhiteSpace(Biography);

    /// <summary><see langword="true"/> when <see cref="PhotoUrl"/> contains something other than whitespace.</summary>
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
/// <param name="BiographyMatch">Candidate whose biography to copy, or <see langword="null"/> to leave the biography.</param>
/// <param name="PhotoMatch">Candidate whose photo to copy, or <see langword="null"/> to leave the photo.</param>
public record AuthorMetadataImportRequest(
    ExternalAuthorMatchDto? BiographyMatch,
    ExternalAuthorMatchDto? PhotoMatch);

/// <summary>Outcome of importing external author metadata into Shelfwarden.</summary>
/// <param name="AuthorId">Author that was updated.</param>
/// <param name="Provider">Sources that were used. Two sources are joined with " + ".</param>
/// <param name="ProviderId">The biography source's id when a biography was imported; otherwise the photo source's id.</param>
/// <param name="BiographyUpdated"><see langword="true"/> when the biography was replaced.</param>
/// <param name="PhotoUpdated"><see langword="true"/> when a photo was saved.</param>
/// <param name="LinkAdded"><see langword="true"/> when a link to the source was added.</param>
public record AuthorMetadataImportResultDto(
    int AuthorId,
    string Provider,
    string ProviderId,
    bool BiographyUpdated,
    bool PhotoUpdated,
    bool LinkAdded);

/// <summary>Outcome of manually updating author biography and/or photo.</summary>
/// <param name="AuthorId">Author that was updated.</param>
/// <param name="Biography">Biography now stored, or <see langword="null"/> when none is stored.</param>
/// <param name="BiographyUpdated"><see langword="true"/> when the biography changed.</param>
/// <param name="PhotoUpdated"><see langword="true"/> when the photo changed.</param>
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
/// <param name="Id">Author id.</param>
/// <param name="Name">Author name.</param>
/// <param name="Biography">Biography, or <see langword="null"/>.</param>
/// <param name="BookCount">Books credited to this author.</param>
/// <param name="SeriesGroups">One group per series this author has a book in.</param>
/// <param name="StandaloneBooks">Books that are not in a series.</param>
/// <param name="PrimaryAuthor">The author this one is a pseudonym of, or <see langword="null"/>.</param>
/// <param name="Pseudonyms">Names that point at this author.</param>
/// <param name="Links">External links.</param>
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
/// <param name="SeriesId">Series id.</param>
/// <param name="SeriesName">Series name.</param>
/// <param name="BookCount">This author's books in the series.</param>
/// <param name="Covers">Up to four covers, in series order. A path is <see langword="null"/> when that book has no cover.</param>
public record AuthorSeriesGroupDto(
    int SeriesId,
    string SeriesName,
    int BookCount,
    IReadOnlyList<SeriesCoverDto> Covers);

/// <summary>Tuple of (book id, cover path) used to build series collages without a second fetch.</summary>
/// <param name="BookId">Book the cover belongs to.</param>
/// <param name="CoverImagePath">Relative cover path, or <see langword="null"/> when that book has no cover.</param>
public record SeriesCoverDto(int BookId, string? CoverImagePath);

/// <summary>
/// All series a user can browse. Mirrors <see cref="AuthorListItemDto"/> in shape — name +
/// counts + a few covers — so the listing page can be built from one query.
/// </summary>
/// <param name="Id">Series id.</param>
/// <param name="Name">Series name.</param>
/// <param name="Description">Description, or <see langword="null"/>.</param>
/// <param name="BookCount">Books in the series.</param>
/// <param name="Covers">Up to four covers, in series order. A path is <see langword="null"/> when that book has no cover.</param>
/// <param name="UniverseId">Universe the series belongs to, or <see langword="null"/>.</param>
/// <param name="UniverseName">That universe's name, or <see langword="null"/> when <paramref name="UniverseId"/> is.</param>
public record SeriesListItemDto(
    int Id,
    string Name,
    string? Description,
    int BookCount,
    IReadOnlyList<SeriesCoverDto> Covers,
    int? UniverseId = null,
    string? UniverseName = null);