namespace Shelfwarden.Services;

/// <summary>
/// Small JPEG versions of book covers for clients on slow links (e-readers browsing OPDS).
/// Generated on first request and cached on disk next to the covers.
/// </summary>
public interface ICoverThumbnailService
{
    /// <summary>
    /// Absolute path to a thumbnail of <paramref name="coverPath"/> whose longest edge is at most
    /// <paramref name="maxSize"/> pixels, creating or refreshing it when the cover is newer.
    /// Null when the cover can't be read as an image.
    /// </summary>
    Task<string?> GetThumbnailPathAsync(int bookId, string coverPath, int maxSize, CancellationToken cancellationToken = default);
}
