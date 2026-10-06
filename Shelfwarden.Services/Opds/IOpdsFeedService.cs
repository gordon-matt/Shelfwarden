using Shelfwarden.Models.Opds;

namespace Shelfwarden.Services.Opds;

/// <summary>
/// Builds OPDS 1.2 catalogue feeds for the calling user. Every feed only contains books on shelves
/// the user can access (<see cref="IShelfAccessService"/>), and list feeds are paged in the database.
/// Page numbers are 1-based; out-of-range values are clamped rather than rejected.
/// </summary>
public interface IOpdsFeedService
{
    /// <summary>Root navigation feed linking to every other feed, plus search.</summary>
    Task<Result<OpdsFeed>> GetRootAsync(OpdsLinkBuilder links, CancellationToken cancellationToken = default);

    /// <summary>Books, newest first.</summary>
    Task<Result<OpdsFeed>> GetRecentAsync(OpdsLinkBuilder links, int page, CancellationToken cancellationToken = default);

    /// <summary>Every book, by sort title.</summary>
    Task<Result<OpdsFeed>> GetAllBooksAsync(OpdsLinkBuilder links, int page, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authors with at least one visible book. Pseudonyms are listed as authors in their own right
    /// (as on the web Authors page), each entry noting the pen-name relationship.
    /// </summary>
    Task<Result<OpdsFeed>> GetAuthorsAsync(OpdsLinkBuilder links, int page, CancellationToken cancellationToken = default);

    /// <summary>Books credited to exactly this author or pseudonym, in series order.</summary>
    Task<Result<OpdsFeed>> GetAuthorBooksAsync(OpdsLinkBuilder links, int authorId, int page, CancellationToken cancellationToken = default);

    /// <summary>Series with at least one visible book.</summary>
    Task<Result<OpdsFeed>> GetSeriesListAsync(OpdsLinkBuilder links, int page, CancellationToken cancellationToken = default);

    /// <summary>Books in a series, in reading order.</summary>
    Task<Result<OpdsFeed>> GetSeriesBooksAsync(OpdsLinkBuilder links, int seriesId, int page, CancellationToken cancellationToken = default);

    /// <summary>Shelves the user can access.</summary>
    Task<Result<OpdsFeed>> GetShelvesAsync(OpdsLinkBuilder links, CancellationToken cancellationToken = default);

    /// <summary>Books on one shelf; not found when the user can't access it.</summary>
    Task<Result<OpdsFeed>> GetShelfBooksAsync(OpdsLinkBuilder links, int shelfId, int page, CancellationToken cancellationToken = default);

    /// <summary>
    /// Books whose title, series, or author matches <paramref name="query"/> (case-insensitive
    /// substring). Author matching covers pseudonyms and, for a pseudonym, its primary author's
    /// name. A blank query yields an empty feed.
    /// </summary>
    Task<Result<OpdsFeed>> SearchAsync(OpdsLinkBuilder links, string? query, int page, CancellationToken cancellationToken = default);

    OpenSearchDescription GetOpenSearchDescription(OpdsLinkBuilder links);
}
