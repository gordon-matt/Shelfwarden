using Hangfire;

namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Walks library folders, parses ebook files, and persists <see cref="Data.Entities.Book"/>s.
/// Always invoked through Hangfire (see <see cref="ILibraryService.ScheduleScanAsync"/>) to
/// avoid blocking request threads on long-running I/O. The scan runs without a user context —
/// authorisation is enforced by the API entry point that schedules the job.
/// </summary>
public interface IScannerService
{
    /// <summary>
    /// Scan a single library. Returns <see cref="Result.NotFound()"/> when the library has been
    /// deleted between enqueue and execution; otherwise returns the populated <see cref="ScanResult"/>.
    /// Hangfire serialises calls per library via the queue + a distributed lock so the same
    /// library cannot scan twice in parallel.
    /// </summary>
    [Queue(Constants.HangfireQueues.Scan)]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    [AutomaticRetry(Attempts = 0)]
    Task<Result<ScanResult>> ScanLibraryAsync(int libraryId, CancellationToken cancellationToken = default);
}