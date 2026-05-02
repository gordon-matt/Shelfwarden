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
    /// Scan a single shelf. Returns <see cref="Result.NotFound()"/> when the shelf has been
    /// deleted between enqueue and execution; otherwise returns the populated <see cref="ScanResult"/>.
    /// Hangfire serialises calls per shelf via the queue + a distributed lock so the same
    /// shelf cannot scan twice in parallel.
    /// </summary>
    [Queue(Constants.HangfireQueues.Scan)]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    [AutomaticRetry(Attempts = 0)]
    Task<Result<ScanResult>> ScanShelfAsync(int shelfId, CancellationToken cancellationToken = default);
}
