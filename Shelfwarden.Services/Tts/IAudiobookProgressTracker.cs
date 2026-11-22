using System.Collections.Concurrent;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// In-process registry of in-flight audiobook generations. The TTS Hangfire worker writes
/// fine-grained chunk progress here; the UI poller reads it via <see cref="IAudiobookService"/> so
/// the progress bar updates more frequently than the database row would.
/// </summary>
public interface IAudiobookProgressTracker
{
    /// <summary>Begin tracking a generation for this book, replacing any counters already stored for it.</summary>
    void Start(int bookId, string voiceName);

    /// <summary>Applies the update to this book's counters. Does nothing when that book is not being tracked.</summary>
    void Update(int bookId, Action<AudiobookProgressBuilder> update);

    /// <summary>Stop tracking this book. The next read returns <see langword="null"/>.</summary>
    void Finish(int bookId);

    /// <summary>The latest counters for this book, or <see langword="null"/> when it is not being tracked.</summary>
    AudiobookLiveProgress? GetSnapshot(int bookId);
}

/// <summary>Mutable view used by the worker to push counters in.</summary>
public sealed class AudiobookProgressBuilder
{
    public int TotalChunks { get; set; }

    public int CompletedChunks { get; set; }

    public string? CurrentStage { get; set; }
}

/// <summary>Immutable snapshot the UI reads each poll.</summary>
public sealed record AudiobookLiveProgress(
    int BookId,
    string VoiceName,
    int TotalChunks,
    int CompletedChunks,
    string? CurrentStage,
    DateTime StartedAt);

/// <inheritdoc cref="IAudiobookProgressTracker"/>
public sealed class AudiobookProgressTracker : IAudiobookProgressTracker
{
    private readonly ConcurrentDictionary<int, Entry> entries = new();

    public void Start(int bookId, string voiceName)
        => entries[bookId] = new Entry(voiceName, new AudiobookProgressBuilder(), DateTime.UtcNow);

    public void Update(int bookId, Action<AudiobookProgressBuilder> update)
    {
        if (!entries.TryGetValue(bookId, out var entry))
        {
            return;
        }

        update(entry.Builder);
        entries[bookId] = entry;
    }

    public void Finish(int bookId) => entries.TryRemove(bookId, out _);

    public AudiobookLiveProgress? GetSnapshot(int bookId)
        => entries.TryGetValue(bookId, out var entry)
            ? new AudiobookLiveProgress(
                bookId,
                entry.VoiceName,
                entry.Builder.TotalChunks,
                entry.Builder.CompletedChunks,
                entry.Builder.CurrentStage,
                entry.StartedAt)
            : null;

    private sealed record Entry(string VoiceName, AudiobookProgressBuilder Builder, DateTime StartedAt);
}