using Heroicsoft.NetForge.Drawing;
using Shelfwarden.Services.Storage;

namespace Shelfwarden.Services;

public sealed class CoverThumbnailService(
    ILogger<CoverThumbnailService> logger,
    IStoragePathProvider storage,
    IImageProcessor images) : ICoverThumbnailService
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

            var info = await images.TryIdentifyAsync(coverPath, cancellationToken);
            if (info is null)
            {
                throw new RasterDecodeException("The cover is not a decodable image.");
            }

            ProcessedImage processed = info.Width > size || info.Height > size
                ? await images.ResizeAsync(coverPath, new ImageTransform
                {
                    Width = size,
                    Height = size,
                    Mode = ImageScaleMode.Max,
                    Format = RasterFormat.Jpeg,
                    Quality = 80,
                }, cancellationToken)
                : await images.TranscodeAsync(coverPath, new ImageEncode(RasterFormat.Jpeg, 80), cancellationToken);

            // Write beside the target then swap in, so a concurrent request never serves half a file.
            string tempPath = $"{thumbnailPath}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(tempPath, processed.Bytes, cancellationToken);
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