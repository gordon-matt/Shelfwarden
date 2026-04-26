namespace Shelfwarden.Models;

/// <summary>
/// Aggregated data shown on the home dashboard. Combines headline counts with carousels
/// of "Continue Reading" and "Recently Added" books so the UI only needs one round-trip.
/// </summary>
public record DashboardDto(
    DashboardStatsDto Stats,
    IReadOnlyList<BookListItemDto> ContinueReading,
    IReadOnlyList<BookListItemDto> RecentlyAdded);

public record DashboardStatsDto(
    int LibraryCount,
    int BookCount,
    int AuthorCount,
    int SeriesCount,
    int FinishedCount);
