namespace Shelfwarden.Services;

/// <summary>
/// Read-only window over per-shelf scan state. Combines the persisted
/// <c>Shelf.LastScannedAt</c> column with live Hangfire monitoring data, so the UI can show
/// "Running...", "Queued", or "Last scanned 5 minutes ago" without polling Hangfire directly.
/// </summary>
public interface IScanStatusService
{
    Task<Result<ScanStatusDto>> GetForShelfAsync(int shelfId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyDictionary<int, ScanStatusDto>>> GetAllAsync(CancellationToken cancellationToken = default);
}