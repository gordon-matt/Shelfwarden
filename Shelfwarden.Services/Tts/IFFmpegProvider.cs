namespace Shelfwarden.Services.Tts;

/// <summary>
/// Lazy provider for FFmpeg binaries. The Xabe.FFmpeg wrapper needs to know where the
/// FFmpeg executable lives on disk; rather than asking operators to install it system-wide
/// we ship <c>Xabe.FFmpeg.Downloader</c> and download the official static build into the
/// TTS cache on first use.
/// </summary>
public interface IFFmpegProvider
{
    /// <summary>
    /// Ensures FFmpeg is installed in the cache directory and configured on the Xabe wrapper.
    /// Subsequent calls are no-ops.
    /// </summary>
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);
}
