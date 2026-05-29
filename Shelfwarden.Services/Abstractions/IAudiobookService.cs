namespace Shelfwarden.Services;

/// <summary>
/// Front-of-house API used by the book detail page. Wraps the
/// <c>Audiobook</c> entity for read access (status polling) and dispatches to <c>ITtsJobService</c>
/// via Hangfire when an administrator requests generation. Listing voices and managing jobs
/// are administrator-only; status and playback follow normal shelf access. The actual
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
    /// Enqueue a Hangfire TTS job for <paramref name="bookId"/> (administrators only). Creates an
    /// <c>Audiobook</c> row in <see cref="AudiobookState.Pending"/> if needed, and reuses
    /// the existing one (resuming previously-completed chunks) when a partial generation
    /// is already on disk.
    /// </summary>
    Task<Result<AudiobookDto>> GenerateAsync(int bookId, GenerateAudiobookRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parse <paramref name="bookId"/> into reviewable sections (chapters + auto-detected front /
    /// back matter) so the user can choose what to skip and where to split (administrators only).
    /// </summary>
    Task<Result<SectionDetectionResult>> GetSectionsAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>List Kokoro voices (administrators only).</summary>
    Task<Result<IReadOnlyList<KokoroVoiceDto>>> GetVoicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate or reuse a short voice-preview WAV for <paramref name="voiceName"/> (administrators only).
    /// </summary>
    Task<Result<string>> EnsureVoiceSampleAsync(string voiceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the finished <c>.m4a</c> and database row for <paramref name="bookId"/> (administrators only)
    /// so an operator can generate again with another voice. Only when generation has completed
    /// (not while queued or running — use <see cref="CancelAsync"/>).
    /// </summary>
    Task<Result> DeleteAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop a queued or in-progress generation, or clear a failed attempt (administrators only):
    /// deletes the audiobook row, working files, and attempts to dequeue the Hangfire job when still pending.
    /// </summary>
    Task<Result> CancelAsync(int bookId, CancellationToken cancellationToken = default);
}