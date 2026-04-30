using Shelfwarden.Services.Storage;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Default <see cref="IFFmpegProvider"/>. Stores the downloaded FFmpeg binaries under
/// <see cref="IStoragePathProvider.TtsCacheDirectory"/>/<c>ffmpeg</c> so they persist across
/// container restarts (when the operator mounts the storage directory as a volume) and
/// so we don't need root access on the host.
/// </summary>
public sealed class FFmpegProvider(
    ILogger<FFmpegProvider> logger,
    IStoragePathProvider storage) : IFFmpegProvider
{
    private readonly SemaphoreSlim initLock = new(1, 1);
    private bool ready;

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (ready)
        {
            return;
        }

        await initLock.WaitAsync(cancellationToken);
        try
        {
            if (ready)
            {
                return;
            }

            string ffmpegDir = Path.Combine(storage.TtsCacheDirectory, "ffmpeg");
            Directory.CreateDirectory(ffmpegDir);

            // Tell Xabe both where to download to and where to look for the executable. The
            // downloader skips the network call when binaries already exist on disk.
            FFmpeg.SetExecutablesPath(ffmpegDir);

            if (!HasFFmpegBinary(ffmpegDir))
            {
                logger.LogInformation("FFmpeg binaries not found in {Path}; downloading official build", ffmpegDir);
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegDir);
                logger.LogInformation("FFmpeg installed at {Path}", ffmpegDir);
            }

            ready = true;
        }
        finally
        {
            initLock.Release();
        }
    }

    private static bool HasFFmpegBinary(string ffmpegDir)
    {
        // Xabe drops the binary as either ffmpeg.exe (Windows) or ffmpeg (POSIX) directly under
        // the configured path. A simple existence check avoids the network round-trip every
        // job startup.
        string[] candidates = OperatingSystem.IsWindows()
            ? ["ffmpeg.exe"]
            : ["ffmpeg"];

        return candidates.Any(name => File.Exists(Path.Combine(ffmpegDir, name)));
    }
}
