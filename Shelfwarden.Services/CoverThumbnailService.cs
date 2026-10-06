using Shelfwarden.Services.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Shelfwarden.Services;

public sealed class CoverThumbnailService(
    ILogger<CoverThumbnailService> logger,
    IStoragePathProvider storage) : ICoverThumbnailService
{
    private const string ThumbnailFolder = "_thumbs";

    public async Task<string?> GetThumbnailPathAsync(int bookId, string coverPath, int maxSize, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(coverPath))
        {
            return null;
        }

        int size = Math.Clamp(maxSize, 32, 2048);
        string directory = Path.Combine(storage.CoversDirectory, ThumbnailFolder);
        string thumbnailPath = Path.Combine(directory, $"{bookId}_{size}.jpg");

        if (File.Exists(thumbnailPath) && File.GetLastWriteTimeUtc(thumbnailPath) >= File.GetLastWriteTimeUtc(coverPath))
        {
            return thumbnailPath;
        }

        try
        {
            Directory.CreateDirectory(directory);

            using var image = await Image.LoadAsync(coverPath, cancellationToken);
            if (image.Width > size || image.Height > size)
            {
                image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(size, size), Mode = ResizeMode.Max }));
            }

            // Write beside the target then swap in, so a concurrent request never serves half a file.
            string tempPath = $"{thumbnailPath}.{Guid.NewGuid():N}.tmp";
            await image.SaveAsJpegAsync(tempPath, new JpegEncoder { Quality = 80 }, cancellationToken);
            File.Move(tempPath, thumbnailPath, overwrite: true);
            return thumbnailPath;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not create a cover thumbnail for book {BookId}", bookId);
            return null;
        }
    }
}
