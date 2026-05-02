namespace Shelfwarden.Services;

/// <summary>
/// Front-of-house API used by the book detail page. Wraps the
/// <c>Audiobook</c> entity for read access (status polling, listing voices) and dispatches
/// to <c>ITtsJobService</c> via Hangfire when the user requests generation. The actual
/// synthesis pipeline never runs in this service — it's strictly an entrypoint.
/// </summary>
public interface IAudiobookService
{
    /// <summary>
    /// Returns the current audiobook status for <paramref name="bookId"/>, or a status of
    /// <see cref="AudiobookState.None"/> when there's no row yet (the user has never
    /// requested generation for this book).
    /// </summary>
    Task<Result<AudiobookDto>> GetStatusAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueue a Hangfire TTS job for <paramref name="bookId"/>. Creates an
    /// <c>Audiobook</c> row in <see cref="AudiobookState.Pending"/> if needed, and reuses
    /// the existing one (resuming previously-completed chunks) when a partial generation
    /// is already on disk.
    /// </summary>
    Task<Result<AudiobookDto>> GenerateAsync(int bookId, GenerateAudiobookRequest request, CancellationToken cancellationToken = default);

    /// <summary>List every voice the loaded Kokoro model can speak as.</summary>
    Task<Result<IReadOnlyList<KokoroVoiceDto>>> GetVoicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a short voice-preview WAV for <paramref name="voiceName"/> if one isn't
    /// already cached, and return the absolute path the caller should stream from.
    /// </summary>
    Task<Result<string>> EnsureVoiceSampleAsync(string voiceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the finished <c>.m4a</c> and database row for <paramref name="bookId"/> so the user
    /// can generate again with another voice. Only allowed when generation has completed (not
    /// while queued or running — use <see cref="CancelAsync"/>).
    /// </summary>
    Task<Result> DeleteAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop a queued or in-progress generation, or clear a failed attempt: deletes the
    /// audiobook row, working files, and attempts to dequeue the Hangfire job when still pending.
    /// </summary>
    Task<Result> CancelAsync(int bookId, CancellationToken cancellationToken = default);
}
