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
    /// Run the full pipeline (extract → chunk → synthesise → encode) for the given
    /// <paramref name="bookId"/>. The voice is pulled off the existing <c>Audiobook</c> row
    /// rather than being passed in, so a re-enqueue picks up whatever the user picked
    /// last.
    /// </summary>
    [Queue(Constants.HangfireQueues.Tts)]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60 * 6)]
    [AutomaticRetry(Attempts = 0)]
    Task GenerateAsync(int bookId, CancellationToken cancellationToken = default);
}
