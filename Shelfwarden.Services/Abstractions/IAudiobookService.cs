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
    /// List every audiobook across the library with its current state and the book it belongs to
    /// (administrators only). Ordered with active generations first, then most recently created.
    /// Powers the admin audiobooks management page.
    /// </summary>
    Task<Result<IReadOnlyList<AudiobookSummaryDto>>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueue a Hangfire TTS job for <paramref name="bookId"/> (administrators only). Creates an
    /// <c>Audiobook</c> row in <see cref="AudiobookState.Pending"/> if needed, and reuses
    /// the existing one (resuming previously-completed chunks) when a partial generation
    /// is already on disk.
    /// </summary>
    Task<Result<AudiobookDto>> GenerateAsync(int bookId, GenerateAudiobookRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parse <paramref name="bookId"/> into reviewable sections (chapters + auto-detected front /
    /// back matter) so the user can choose what to skip and where to split (administrators only). Any
    /// previously saved selection for this book is re-applied on top of the detected sections.
    /// </summary>
    Task<Result<AudiobookPlanDto>> GetSectionsAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>List Kokoro voices (administrators only).</summary>
    Task<Result<IReadOnlyList<KokoroVoiceDto>>> GetVoicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate or reuse a short voice-preview WAV for <paramref name="voiceName"/> (administrators only).
    /// </summary>
    Task<Result<string>> EnsureVoiceSampleAsync(string voiceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the finished audio for <paramref name="bookId"/> and marks the row deleted
    /// (administrators only). The voice, chapter split, and section plan are kept for the next
    /// generation. Only a completed audiobook can be deleted this way. A queued or running one
    /// has to be stopped with <see cref="CancelAsync"/> first, and a failed one is cleared the
    /// same way.
    /// </summary>
    Task<Result> DeleteAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove one chapter file from a completed split audiobook (administrators only). Updates
    /// <c>ChaptersJson</c> and aggregate size/duration. If no chapters remain, the audiobook row
    /// is removed so the user can generate again.
    /// </summary>
    Task<Result<AudiobookDto>> DeleteChapterAsync(int bookId, int chapterIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a queued or in-progress generation, or clears a failed attempt (administrators only).
    /// The output and working files are removed and the row is marked deleted, keeping the voice,
    /// chapter split, and section plan. A Hangfire job that is still queued is removed. A finished
    /// audiobook is refused; use <see cref="DeleteAsync"/> for that.
    /// </summary>
    Task<Result> CancelAsync(int bookId, CancellationToken cancellationToken = default);
}