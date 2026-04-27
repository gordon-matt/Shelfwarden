using System.Globalization;
using Hangfire;
using Hangfire.Storage.Monitoring;

namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Default <see cref="IScanStatusService"/>. Joins per-library <c>LastScannedAt</c> values from
/// the database with live job state from Hangfire's monitoring API.
/// <para>
/// Hangfire's monitoring API can be expensive on large queues — call sites should debounce
/// (e.g. UI polls at most every few seconds, not on every render).
/// </para>
/// </summary>
public class ScanStatusService(
    JobStorage jobStorage,
    IScanProgressTracker progressTracker,
    IRepository<Library> libraryRepository) : IScanStatusService
{
    private const string ScanMethodName = nameof(IScannerService.ScanLibraryAsync);

    public async Task<Result<ScanStatusDto>> GetForLibraryAsync(int libraryId, CancellationToken cancellationToken = default)
    {
        var library = await libraryRepository.FindOneAsync(new SearchOptions<Library>
        {
            Query = l => l.Id == libraryId,
            CancellationToken = cancellationToken,
        });
        if (library is null)
        {
            return Result.NotFound();
        }

        var (running, queued) = GetActiveLibraryIds();
        var state = ResolveState(libraryId, running, queued, library.LastScannedAt);
        var progress = state == ScanState.Running ? progressTracker.GetSnapshot(libraryId) : null;
        return Result.Success(new ScanStatusDto(libraryId, state, library.LastScannedAt, progress));
    }

    public async Task<Result<IReadOnlyDictionary<int, ScanStatusDto>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var libraries = await libraryRepository.FindAsync(new SearchOptions<Library>
        {
            CancellationToken = cancellationToken,
        });

        var (running, queued) = GetActiveLibraryIds();

        var map = libraries.ToDictionary(
            l => l.Id,
            l =>
            {
                var state = ResolveState(l.Id, running, queued, l.LastScannedAt);
                var progress = state == ScanState.Running ? progressTracker.GetSnapshot(l.Id) : null;
                return new ScanStatusDto(l.Id, state, l.LastScannedAt, progress);
            });

        return Result.Success<IReadOnlyDictionary<int, ScanStatusDto>>(map);
    }

    private static ScanState ResolveState(int libraryId, HashSet<int> running, HashSet<int> queued, DateTime? lastScannedAt)
    {
        // Order matters: a queue can momentarily contain both an enqueued retry and a running
        // job, but the user cares most about "is something happening right now?".
        if (running.Contains(libraryId)) return ScanState.Running;
        if (queued.Contains(libraryId)) return ScanState.Queued;
        return lastScannedAt.HasValue ? ScanState.Succeeded : ScanState.Idle;
    }

    private (HashSet<int> Running, HashSet<int> Queued) GetActiveLibraryIds()
    {
        var monitoring = jobStorage.GetMonitoringApi();

        var running = new HashSet<int>();
        var queued = new HashSet<int>();

        try
        {
            // Scan queue is reserved for scan jobs (see Constants.HangfireQueues.Scan), so we
            // know args[0] is the library id and don't need to filter by method name.
            foreach (var pair in monitoring.ProcessingJobs(0, 200))
            {
                if (TryGetLibraryId(pair.Value?.Job, out int id))
                {
                    running.Add(id);
                }
            }

            long enqueued = monitoring.EnqueuedCount(Constants.HangfireQueues.Scan);
            if (enqueued > 0)
            {
                foreach (var pair in monitoring.EnqueuedJobs(Constants.HangfireQueues.Scan, 0, (int)Math.Min(enqueued, 200)))
                {
                    if (TryGetLibraryId(pair.Value?.Job, out int id))
                    {
                        queued.Add(id);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Monitoring data is best-effort: if the storage backend is misbehaving we'd rather
            // show a slightly stale status than tear down the whole UI. Treat as "no jobs in flight".
        }

        return (running, queued);
    }

    private static bool TryGetLibraryId(Hangfire.Common.Job? job, out int libraryId)
    {
        libraryId = 0;
        if (job?.Method?.Name != ScanMethodName) return false;
        if (job.Args is null || job.Args.Count == 0) return false;

        // Hangfire deserialises ints back to int, but be defensive about strings too.
        try
        {
            libraryId = job.Args[0] switch
            {
                int i => i,
                long l => (int)l,
                string s => int.Parse(s, CultureInfo.InvariantCulture),
                _ => Convert.ToInt32(job.Args[0], CultureInfo.InvariantCulture),
            };
            return libraryId > 0;
        }
        catch
        {
            return false;
        }
    }
}
