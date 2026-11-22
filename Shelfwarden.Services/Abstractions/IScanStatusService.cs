namespace Shelfwarden.Services;

/// <summary>
/// Read-only window over per-shelf scan state. Combines the persisted
/// <c>Shelf.LastScannedAt</c> column with live Hangfire monitoring data, so the UI can show
/// "Running...", "Queued", or "Last scanned 5 minutes ago" without polling Hangfire directly.
/// </summary>
public interface IScanStatusService
{
    /// <summary>
    /// Returns the shelf's scan state, or not found when the shelf is missing. A progress snapshot,
    /// or a running <see cref="Scanning.IScannerService.ScanShelfAsync"/> job, is
    /// <see cref="ScanState.Running"/>. A job that is only waiting is <see cref="ScanState.Queued"/>.
    /// Otherwise a shelf with a last-scanned time is <see cref="ScanState.Succeeded"/>, and one
    /// without is <see cref="ScanState.Idle"/>.
    /// </summary>
    /// <remarks>
    /// The Hangfire check looks at the first 200 processing jobs and the first 200 queued jobs on
    /// each queue. If that check throws, no job is treated as running or queued. A progress snapshot
    /// still counts as running.
    /// </remarks>
    /// <param name="shelfId">Shelf id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The scan state, or not found.</returns>
    Task<Result<ScanStatusDto>> GetForShelfAsync(int shelfId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one <see cref="ScanStatusDto"/> for every shelf, keyed by shelf id. The state rules
    /// match <see cref="GetForShelfAsync"/>.
    /// </summary>
    /// <remarks>
    /// Uses the same Hangfire check as <see cref="GetForShelfAsync"/>: the first 200 processing jobs
    /// and the first 200 queued jobs on each queue. If that check throws, no job is treated as
    /// running or queued.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One status per shelf, keyed by shelf id.</returns>
    Task<Result<IReadOnlyDictionary<int, ScanStatusDto>>> GetAllAsync(CancellationToken cancellationToken = default);
}