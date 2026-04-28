namespace Shelfwarden.Services;

/// <summary>
/// Read-only window over per-library scan state. Combines the persisted
/// <c>Library.LastScannedAt</c> column with live Hangfire monitoring data, so the UI can show
/// "Running...", "Queued", or "Last scanned 5 minutes ago" without polling Hangfire directly.
/// </summary>
public interface IScanStatusService
{
    Task<Result<ScanStatusDto>> GetForLibraryAsync(int libraryId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyDictionary<int, ScanStatusDto>>> GetAllAsync(CancellationToken cancellationToken = default);
}