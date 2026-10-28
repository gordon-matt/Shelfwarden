using Heroicsoft.NetForge.Video;
using Shelfwarden.Services.Storage;

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

            // Prefer a distro ffmpeg (the Docker image) and otherwise download an official build
            // into the TTS cache so a container restart does not fetch it again.
            await FfmpegBootstrap.EnsureAsync(
                new FfmpegBootstrapOptions
                {
                    PreferLinuxSystemDirectory = true,
                    DownloadDirectory = Path.Combine(storage.TtsCacheDirectory, "ffmpeg"),
                    SearchWellKnownLocations = false,
                    DownloadIfMissing = true,
                    RequireFfprobe = false,
                },
                logger,
                cancellationToken);

            ready = true;
        }
        finally
        {
            initLock.Release();
        }
    }
}