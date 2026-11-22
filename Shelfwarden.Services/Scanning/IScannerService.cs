using Hangfire;

namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Walks shelf folders, parses ebook files, and persists <see cref="Data.Entities.Book"/>s.
/// Always invoked through Hangfire (see <see cref="IShelfService.ScheduleScanAsync"/>) to
/// avoid blocking request threads on long-running I/O. The scan runs without a user context —
/// authorisation is enforced by the API entry point that schedules the job.
/// </summary>
public interface IScannerService
{
    /// <summary>
    /// Scans one shelf. Returns not found when the shelf was deleted between enqueue and execution.
    /// A lock on this method lets only one scan run at a time; a second scan waits up to an hour.
    /// A failed scan is not retried. Books whose files are no longer under the shelf's folders are
    /// removed, and their cover files are deleted. The ebook files are not deleted by the scan.
    /// </summary>
    [Queue(Constants.HangfireQueues.Scan)]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    [AutomaticRetry(Attempts = 0)]
    Task<Result<ScanResult>> ScanShelfAsync(int shelfId, CancellationToken cancellationToken = default);
}