namespace Shelfwarden.Models;

/// <summary>
/// Aggregated data shown on the home dashboard. Combines headline counts with carousels
/// of "Continue Reading" and "Recently Added" books so the UI only needs one round-trip.
/// </summary>
/// <param name="Stats">Headline counts.</param>
/// <param name="ContinueReading">Books this caller has started and not finished, most recently read first.</param>
/// <param name="RecentlyAdded">Newest books the caller can open.</param>
public record DashboardDto(
    DashboardStatsDto Stats,
    IReadOnlyList<BookListItemDto> ContinueReading,
    IReadOnlyList<BookListItemDto> RecentlyAdded);

/// <summary>
/// Headline counts. Shelf and book counts follow what the caller can open. Author and series counts do not.
/// </summary>
/// <param name="ShelfCount">Shelves the caller can open.</param>
/// <param name="BookCount">Books on those shelves.</param>
/// <param name="AuthorCount">Every author in the library.</param>
/// <param name="SeriesCount">Every series in the library.</param>
/// <param name="FinishedCount">
/// Books this caller has at or above <see cref="Shelfwarden.Constants.FinishedThresholdPercent"/>.
/// Zero when nobody is signed in.
/// </param>
public record DashboardStatsDto(
    int ShelfCount,
    int BookCount,
    int AuthorCount,
    int SeriesCount,
    int FinishedCount);