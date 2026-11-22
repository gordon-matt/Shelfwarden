using Hangfire;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Hangfire entrypoint for audiobook synthesis. Always invoked through
/// <see cref="IBackgroundJobClient"/>; the public-facing <c>IAudiobookService</c> calls into
/// this rather than running synthesis inline because each generation can take hours.
/// </summary>
public interface ITtsJobService
{
    /// <summary>
    /// Runs the full pipeline (extract, chunk, synthesise, encode) for <paramref name="bookId"/>.
    /// The voice comes from the stored audiobook row, so a re-enqueue uses whatever was picked last.
    /// Only one generation runs at a time; a second waits up to six hours for the lock. A failed
    /// run is not retried.
    /// </summary>
    [Queue(Constants.HangfireQueues.Tts)]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60 * 6)]
    [AutomaticRetry(Attempts = 0)]
    Task GenerateAsync(int bookId, CancellationToken cancellationToken = default);
}