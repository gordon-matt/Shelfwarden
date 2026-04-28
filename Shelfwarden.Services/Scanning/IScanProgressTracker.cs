using System.Collections.Concurrent;

namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Process-local registry of in-flight scan progress per library. Populated by
/// <see cref="ScannerService"/> as it walks the filesystem; read by
/// <see cref="ScanStatusService"/> so the libraries page can surface live counters.
/// <para>
/// Singleton lifetime — a single shared instance keeps state across Hangfire workers
/// (which run in the same process) and Blazor circuits (which read the snapshot every poll).
/// </para>
/// </summary>
public interface IScanProgressTracker
{
    /// <summary>Begin tracking a scan for <paramref name="libraryId"/>. Resets any previous progress.</summary>
    void Start(int libraryId);

    /// <summary>Record progress for the active scan. Each call replaces the snapshot.</summary>
    void Update(int libraryId, Action<ScanProgressBuilder> update);

    /// <summary>Stop tracking the scan; the next read returns null.</summary>
    void Finish(int libraryId);

    ScanProgressDto? GetSnapshot(int libraryId);

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

    public void Start(int libraryId)
    {
        var now = DateTime.UtcNow;
        entries[libraryId] = new Entry(
            new ScanProgressBuilder(),
            now);
    }

    public void Update(int libraryId, Action<ScanProgressBuilder> update)
    {
        // Lookup-and-replace: callers run on a single scan worker per library, so we don't need
        // to retry on concurrent writes. The dictionary key is the only contention point.
        if (!entries.TryGetValue(libraryId, out var existing))
        {
            return;
        }

        update(existing.Builder);
        entries[libraryId] = existing;
    }

    public void Finish(int libraryId) => entries.TryRemove(libraryId, out _);

    public ScanProgressDto? GetSnapshot(int libraryId)
        => entries.TryGetValue(libraryId, out var entry) ? entry.ToDto(libraryId) : null;

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