namespace Shelfwarden.Services;

/// <summary>
/// Aggregates the data shown on the home dashboard. Lives in the service layer rather than
/// being assembled by the page so the same data can be served to a future API client.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Shelf and book counts for shelves the caller can open, the library's author and series
    /// totals, how many books the caller has finished, the twenty newest books they can open,
    /// and up to ten books they are still reading. Finished means progress at or above
    /// <see cref="Constants.FinishedThresholdPercent"/>. A book still counts as in progress when
    /// its percentage is below that and either the percentage is above zero or an EPUB location
    /// is saved.
    /// </summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The dashboard.</returns>
    Task<Result<DashboardDto>> GetAsync(CancellationToken cancellationToken = default);
}