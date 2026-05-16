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

    /// <summary>
    /// Absolute path to the directory where finished audiobook files (.m4a) are stored. Files
    /// are named <c>{bookId}.m4a</c> so they can be located deterministically from a book id.
    /// </summary>
    string AudiobooksDirectory { get; }

    /// <summary>
    /// Absolute path to a per-book working directory used while a TTS job is running.
    /// Created on demand. Holds raw PCM chunk files so partial work survives a process
    /// restart and the job can resume from the next missing chunk.
    /// </summary>
    string GetAudiobookWorkingDirectory(int bookId);

    /// <summary>
    /// Absolute path to the cached short voice-preview WAV for <paramref name="voiceName"/>.
    /// File may not yet exist; the caller is responsible for generating it on first use.
    /// </summary>
    string GetVoiceSamplePath(string voiceName);

    /// <summary>
    /// Absolute path to the directory KokoroSharp / FFmpeg downloads land in. Lives under the
    /// app data root so the binaries persist across builds and Docker layer rebuilds.
    /// </summary>
    string TtsCacheDirectory { get; }

    /// <summary>Absolute path to the encoded audiobook file for <paramref name="bookId"/>, regardless of whether it exists.</summary>
    string GetAudiobookFilePath(int bookId);

    /// <summary>Delete the encoded audiobook + any working files for <paramref name="bookId"/>.</summary>
    void DeleteAudiobook(int bookId);

    /// <summary>
    /// Absolute path to the directory where extra (non-ebook) content files are stored.
    /// Configurable via <c>Storage:ExtrasPath</c>; defaults to a sibling of the covers directory
    /// named <c>_extras</c>.
    /// </summary>
    string ExtrasDirectory { get; }

    /// <summary>Directory for user-uploaded shelf / collection / reading list tile banners.</summary>
    string CardBannersDirectory { get; }

    /// <summary>Delete any uploaded banner for the given entity (any extension).</summary>
    void DeleteCardBannerFile(string kind, int entityId);

    /// <summary>Resolve the on-disk path for a stored relative banner filename, or null.</summary>
    string? GetCardBannerPath(string? relativeFileName);

    /// <summary>Find an uploaded banner for <paramref name="kind"/>/<paramref name="entityId"/> (any extension), or null.</summary>
    string? FindCardBannerFilePath(string kind, int entityId);

    /// <summary>Save an uploaded banner; returns the relative filename to store on the entity.</summary>
    Task<string> SaveCardBannerFileAsync(string kind, int entityId, Stream content, string originalFileName, CancellationToken cancellationToken = default);
}