namespace Shelfwarden.Services;

/// <summary>
/// Aggregates the data shown on the home dashboard. Lives in the service layer rather than
/// being assembled by the page so the same data can be served to a future API client.
/// </summary>
public interface IDashboardService
{
    Task<Result<DashboardDto>> GetAsync(CancellationToken cancellationToken = default);
}