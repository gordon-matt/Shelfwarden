using System.Globalization;
using Hangfire;

namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Default <see cref="IScanStatusService"/>. Joins per-shelf <c>LastScannedAt</c> values from
/// the database with live job state from Hangfire's monitoring API.
/// <para>
/// Hangfire's monitoring API can be expensive on large queues — call sites should debounce
/// (e.g. UI polls at most every few seconds, not on every render).
/// </para>
/// </summary>
public class ScanStatusService(
    JobStorage jobStorage,
    IScanProgressTracker progressTracker,
    IRepository<Shelf> shelfRepository) : IScanStatusService
{
    private const string ScanMethodName = nameof(IScannerService.ScanShelfAsync);

    public async Task<Result<ScanStatusDto>> GetForShelfAsync(int shelfId, CancellationToken cancellationToken = default)
    {
        var shelf = await shelfRepository.FindOneAsync(new SearchOptions<Shelf>
        {
            Query = s => s.Id == shelfId,
            CancellationToken = cancellationToken,
        });
        if (shelf is null)
        {
            return Result.NotFound();
        }

        var (running, queued) = GetActiveShelfIds();
        var progress = progressTracker.GetSnapshot(shelfId);
        var state = progress is not null
            ? ScanState.Running
            : ResolveState(shelfId, running, queued, shelf.LastScannedAt);
        return Result.Success(new ScanStatusDto(shelfId, state, shelf.LastScannedAt, progress));
    }

    public async Task<Result<IReadOnlyDictionary<int, ScanStatusDto>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var shelves = await shelfRepository.FindAsync(new SearchOptions<Shelf>
        {
            CancellationToken = cancellationToken,
        });

        var (running, queued) = GetActiveShelfIds();

        var map = shelves.ToDictionary(
            s => s.Id,
            s =>
            {
                var state = ResolveState(s.Id, running, queued, s.LastScannedAt);
                var progress = progressTracker.GetSnapshot(s.Id);
                if (progress is not null)
                {
                    state = ScanState.Running;
                }
                return new ScanStatusDto(s.Id, state, s.LastScannedAt, progress);
            });

        return Result.Success<IReadOnlyDictionary<int, ScanStatusDto>>(map);
    }

    private static ScanState ResolveState(int shelfId, HashSet<int> running, HashSet<int> queued, DateTime? lastScannedAt) =>
        // Order matters: a queue can momentarily contain both an enqueued retry and a running
        // job, but the user cares most about "is something happening right now?".
        running.Contains(shelfId)
            ? ScanState.Running
            : queued.Contains(shelfId) ? ScanState.Queued : lastScannedAt.HasValue ? ScanState.Succeeded : ScanState.Idle;

    private (HashSet<int> Running, HashSet<int> Queued) GetActiveShelfIds()
    {
        var monitoring = jobStorage.GetMonitoringApi();

        var running = new HashSet<int>();
        var queued = new HashSet<int>();

        try
        {
            // Running jobs: inspect processing across all queues/workers and keep only scanner
            // jobs by method signature. This is more robust than assuming a specific queue name.
            foreach (var pair in monitoring.ProcessingJobs(0, 200))
            {
                if (TryGetShelfId(pair.Value?.Job, out int id))
                {
                    running.Add(id);
                }
            }

            // Enqueued jobs: walk every queue Hangfire knows about (not just "scan"), then
            // filter by ScanShelfAsync. Some installations route jobs differently.
            var queues = monitoring.Queues();
            foreach (var q in queues)
            {
                long enqueued = monitoring.EnqueuedCount(q.Name);
                if (enqueued <= 0)
                {
                    continue;
                }

                foreach (var pair in monitoring.EnqueuedJobs(q.Name, 0, (int)Math.Min(enqueued, 200)))
                {
                    if (TryGetShelfId(pair.Value?.Job, out int id))
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

    private static bool TryGetShelfId(Hangfire.Common.Job? job, out int shelfId)
    {
        shelfId = 0;
        if (job?.Method?.Name != ScanMethodName)
        {
            return false;
        }

        if (job.Args is null || job.Args.Count == 0)
        {
            return false;
        }

        // Hangfire deserialises ints back to int, but be defensive about strings too.
        try
        {
            shelfId = job.Args[0] switch
            {
                int i => i,
                long l => (int)l,
                string s => int.Parse(s, CultureInfo.InvariantCulture),
                _ => Convert.ToInt32(job.Args[0], CultureInfo.InvariantCulture),
            };
            return shelfId > 0;
        }
        catch
        {
            return false;
        }
    }
}
