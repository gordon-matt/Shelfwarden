using System.Collections.Concurrent;

namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Process-local registry of in-flight scan progress per shelf. Populated by
/// <see cref="ScannerService"/> as it walks the filesystem; read by
/// <see cref="ScanStatusService"/> so the shelves page can surface live counters.
/// <para>
/// Singleton lifetime — a single shared instance keeps state across Hangfire workers
/// (which run in the same process) and Blazor circuits (which read the snapshot every poll).
/// </para>
/// </summary>
public interface IScanProgressTracker
{
    /// <summary>Begin tracking a scan for <paramref name="shelfId"/>. Resets any previous progress.</summary>
    void Start(int shelfId);

    /// <summary>Applies the update to this shelf's counters. Does nothing when that shelf is not being tracked.</summary>
    void Update(int shelfId, Action<ScanProgressBuilder> update);

    /// <summary>Stop tracking the scan. The next read returns <see langword="null"/>.</summary>
    void Finish(int shelfId);

    /// <summary>The latest counters for this shelf, or <see langword="null"/> when no scan is being tracked.</summary>
    ScanProgressDto? GetSnapshot(int shelfId);

    /// <summary>The latest counters for every shelf that is still being tracked.</summary>
    IReadOnlyDictionary<int, ScanProgressDto> GetAllSnapshots();
}

/// <summary>Mutable builder used by callers — keeps the public API free of out parameters.</summary>
public sealed class ScanProgressBuilder
{
    public int FilesScanned { get; set; }

    public int BooksAdded { get; set; }

    public int BooksUpdated { get; set; }

    public int BooksRemoved { get; set; }

    public int Errors { get; set; }

    public string? CurrentFile { get; set; }
}

/// <inheritdoc cref="IScanProgressTracker"/>
public sealed class ScanProgressTracker : IScanProgressTracker
{
    // ConcurrentDictionary keeps reads + writes lock-free; the values themselves are immutable
    // records so there's no read/modify/write race against the UI poller.
    private readonly ConcurrentDictionary<int, Entry> entries = new();

    public void Start(int shelfId)
    {
        var now = DateTime.UtcNow;
        entries[shelfId] = new Entry(
            new ScanProgressBuilder(),
            now);
    }

    public void Update(int shelfId, Action<ScanProgressBuilder> update)
    {
        // Lookup-and-replace: callers run on a single scan worker per shelf, so we don't need
        // to retry on concurrent writes. The dictionary key is the only contention point.
        if (!entries.TryGetValue(shelfId, out var existing))
        {
            return;
        }

        update(existing.Builder);
        entries[shelfId] = existing;
    }

    public void Finish(int shelfId) => entries.TryRemove(shelfId, out _);

    public ScanProgressDto? GetSnapshot(int shelfId)
        => entries.TryGetValue(shelfId, out var entry) ? entry.ToDto(shelfId) : null;

    public IReadOnlyDictionary<int, ScanProgressDto> GetAllSnapshots()
        => entries.ToDictionary(kv => kv.Key, kv => kv.Value.ToDto(kv.Key));

    private sealed record Entry(ScanProgressBuilder Builder, DateTime StartedAt)
    {
        public ScanProgressDto ToDto(int _) => new(
            Builder.FilesScanned,
            Builder.BooksAdded,
            Builder.BooksUpdated,
            Builder.BooksRemoved,
            Builder.Errors,
            Builder.CurrentFile,
            StartedAt);
    }
}