using Shelfwarden.Services.Scanning;

namespace Shelfwarden.Services.Storage;

/// <summary>
/// Centralises filesystem path resolution so the rest of the app never builds paths from
/// <c>IConfiguration</c> directly. Provides:
/// <list type="bullet">
///   <item>The covers directory (created on demand).</item>
///   <item>Helpers to write a cover for a book and produce its public URL.</item>
/// </list>
/// </summary>
public interface IStoragePathProvider
{
    /// <summary>Absolute path to the directory where extracted cover images are stored.</summary>
    string CoversDirectory { get; }

    /// <summary>
    /// Absolute path to the directory where uploaded author photos are stored. Files are
    /// named <c>{authorId}.{ext}</c> so they can be served by an authenticated controller.
    /// </summary>
    string AuthorPhotosDirectory { get; }

    /// <summary>
    /// Returns the absolute path to the author's existing photo, regardless of extension, or
    /// null if no photo is present. Used when serving the photo back to the browser.
    /// </summary>
    string? FindAuthorPhotoPath(int authorId);

    /// <summary>Absolute path to the file currently associated with the given book id, or null when none.</summary>
    string? GetCoverFilePath(int bookId, string? extension);

    /// <summary>Persist <paramref name="cover"/> to <see cref="CoversDirectory"/> and return the relative path stored on the book.</summary>
    Task<string> SaveCoverAsync(int bookId, EbookCoverImage cover, CancellationToken cancellationToken = default);

    /// <summary>Delete the cover file (if any) currently associated with <paramref name="relativePath"/>.</summary>
    void DeleteCover(string? relativePath);
}