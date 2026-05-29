using System.Globalization;
using Microsoft.Extensions.Configuration;
using Shelfwarden.Services.Scanning;

namespace Shelfwarden.Services.Storage;

/// <summary>
/// Default <see cref="IStoragePathProvider"/>. Reads <c>Storage:CoversPath</c> from configuration.
/// Relative paths resolve to the application's content root so dev and container deployments
/// behave the same. Falls back to <c>covers</c> next to the binary when unset.
/// </summary>
public sealed class StoragePathProvider : IStoragePathProvider
{
    private readonly ILogger<StoragePathProvider> logger;

    public StoragePathProvider(IConfiguration configuration, ILogger<StoragePathProvider> logger)
    {
        this.logger = logger;

        // Storage:CoversPath is the canonical key; legacy Shelfwarden:CoversPath remains as a fallback.
        string? configured = configuration["Storage:CoversPath"]
            ?? configuration["Shelfwarden:CoversPath"];

        string baseDir = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "covers")
            : Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(AppContext.BaseDirectory, configured);

        CoversDirectory = Path.GetFullPath(baseDir);
        Directory.CreateDirectory(CoversDirectory);

        // Author photos sit alongside the covers directory by default. Configurable for installs
        // where the operator wants to mount a separate volume for them.
        string? authorPhotosConfigured = configuration["Storage:AuthorPhotosPath"];
        string authorPhotosBase = string.IsNullOrWhiteSpace(authorPhotosConfigured)
            ? Path.Combine(Path.GetDirectoryName(CoversDirectory) ?? AppContext.BaseDirectory, "author-photos")
            : Path.IsPathRooted(authorPhotosConfigured)
                ? authorPhotosConfigured
                : Path.Combine(AppContext.BaseDirectory, authorPhotosConfigured);

        AuthorPhotosDirectory = Path.GetFullPath(authorPhotosBase);
        Directory.CreateDirectory(AuthorPhotosDirectory);

        // Audiobooks live alongside covers / author photos by default. Operators on small
        // volumes will likely want to redirect this somewhere bigger because each generated
        // file lands at tens of megabytes.
        string? audiobooksConfigured = configuration["Storage:AudiobooksPath"];
        string audiobooksBase = string.IsNullOrWhiteSpace(audiobooksConfigured)
            ? Path.Combine(Path.GetDirectoryName(CoversDirectory) ?? AppContext.BaseDirectory, "audiobooks")
            : Path.IsPathRooted(audiobooksConfigured)
                ? audiobooksConfigured
                : Path.Combine(AppContext.BaseDirectory, audiobooksConfigured);

        AudiobooksDirectory = Path.GetFullPath(audiobooksBase);
        Directory.CreateDirectory(AudiobooksDirectory);

        // Kokoro model + ffmpeg binaries are large and slow to download. Persist them under
        // the app data root so they survive container restarts when the operator mounts the
        // storage directory as a volume.
        string? ttsCacheConfigured = configuration["Storage:TtsCachePath"];
        string ttsCacheBase = string.IsNullOrWhiteSpace(ttsCacheConfigured)
            ? Path.Combine(Path.GetDirectoryName(CoversDirectory) ?? AppContext.BaseDirectory, "tts-cache")
            : Path.IsPathRooted(ttsCacheConfigured)
                ? ttsCacheConfigured
                : Path.Combine(AppContext.BaseDirectory, ttsCacheConfigured);

        TtsCacheDirectory = Path.GetFullPath(ttsCacheBase);
        Directory.CreateDirectory(TtsCacheDirectory);

        string? extrasConfigured = configuration["Storage:ExtrasPath"];
        string extrasBase = string.IsNullOrWhiteSpace(extrasConfigured)
            ? Path.Combine(Path.GetDirectoryName(CoversDirectory) ?? AppContext.BaseDirectory, "_extras")
            : Path.IsPathRooted(extrasConfigured)
                ? extrasConfigured
                : Path.Combine(AppContext.BaseDirectory, extrasConfigured);

        ExtrasDirectory = Path.GetFullPath(extrasBase);
        Directory.CreateDirectory(ExtrasDirectory);

        string? bannersConfigured = configuration["Storage:CardBannersPath"];
        string bannersBase = string.IsNullOrWhiteSpace(bannersConfigured)
            ? Path.Combine(Path.GetDirectoryName(CoversDirectory) ?? AppContext.BaseDirectory, "card-banners")
            : Path.IsPathRooted(bannersConfigured)
                ? bannersConfigured
                : Path.Combine(AppContext.BaseDirectory, bannersConfigured);

        CardBannersDirectory = Path.GetFullPath(bannersBase);
        Directory.CreateDirectory(CardBannersDirectory);
    }

    public string CoversDirectory { get; }

    public string ExtrasDirectory { get; }

    public string AuthorPhotosDirectory { get; }

    public string AudiobooksDirectory { get; }

    public string TtsCacheDirectory { get; }

    public string CardBannersDirectory { get; }

    public string GetAudiobookWorkingDirectory(int bookId)
    {
        string path = Path.Combine(AudiobooksDirectory, "_tmp", bookId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetAudiobookFilePath(int bookId)
        => Path.Combine(AudiobooksDirectory, $"{bookId}.m4a");

    public string GetAudiobookChapterFilePath(int bookId, int chapterIndex)
    {
        string dir = Path.Combine(AudiobooksDirectory, bookId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{chapterIndex:D4}.m4a");
    }

    public string GetAudiobookChapterWorkingDirectory(int bookId, int chapterIndex)
    {
        string path = Path.Combine(
            GetAudiobookWorkingDirectory(bookId),
            $"ch{chapterIndex:D4}");
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetVoiceSamplePath(string voiceName)
    {
        // Voice names are short ASCII tokens (e.g. "af_heart") so this is safe to use
        // directly as a filename — but sanitise just in case a future voice introduces
        // anything exotic.
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new(voiceName.Where(c => !invalid.Contains(c)).ToArray());
        string dir = Path.Combine(AudiobooksDirectory, "_voicesamples");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{safe}.wav");
    }

    public void DeleteAudiobook(int bookId)
    {
        try
        {
            string filePath = GetAudiobookFilePath(bookId);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            // Per-chapter output directory for split audiobooks.
            string chapterDir = Path.Combine(AudiobooksDirectory, bookId.ToString(CultureInfo.InvariantCulture));
            if (Directory.Exists(chapterDir))
            {
                Directory.Delete(chapterDir, recursive: true);
            }

            string working = Path.Combine(AudiobooksDirectory, "_tmp", bookId.ToString(CultureInfo.InvariantCulture));
            if (Directory.Exists(working))
            {
                Directory.Delete(working, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete audiobook artefacts for book {BookId}", bookId);
        }
    }

    public string? FindAuthorPhotoPath(int authorId)
    {
        try
        {
            return Directory.EnumerateFiles(AuthorPhotosDirectory, $"{authorId}.*")
                .FirstOrDefault();
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public string? GetCoverFilePath(int bookId, string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        string ext = extension.StartsWith('.') ? extension[1..] : extension;
        return Path.Combine(CoversDirectory, $"{bookId}.{ext}");
    }

    public async Task<string> SaveCoverAsync(int bookId, EbookCoverImage cover, CancellationToken cancellationToken = default)
    {
        DeleteAllForBook(bookId);

        string ext = cover.Extension.TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(ext))
        {
            ext = "jpg";
        }

        string filename = $"{bookId}.{ext}";
        string fullPath = Path.Combine(CoversDirectory, filename);

        await File.WriteAllBytesAsync(fullPath, cover.Data, cancellationToken);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Saved cover for book {BookId} -> {Path}", bookId, fullPath);
        }

        // Stored as a relative path so the database is portable across deployments.
        return filename;
    }

    public void DeleteCover(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        string fullPath = Path.Combine(CoversDirectory, relativePath);
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete cover {Path}", fullPath);
        }
    }

    private void DeleteAllForBook(int bookId)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(CoversDirectory, $"{bookId}.*"))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean previous covers for book {BookId}", bookId);
        }
    }

    public void DeleteCardBannerFile(string kind, int entityId)
    {
        try
        {
            string pattern = $"{SanitizeKind(kind)}-{entityId.ToString(CultureInfo.InvariantCulture)}.*";
            foreach (string file in Directory.EnumerateFiles(CardBannersDirectory, pattern))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete card banner {Kind} {Id}", kind, entityId);
        }
    }

    public string? GetCardBannerPath(string? relativeFileName)
    {
        if (string.IsNullOrWhiteSpace(relativeFileName))
        {
            return null;
        }

        string full = Path.GetFullPath(Path.Combine(CardBannersDirectory, relativeFileName));
        string root = Path.GetFullPath(CardBannersDirectory) + Path.DirectorySeparatorChar;
        return !full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full) ? null : full;
    }

    public string? FindCardBannerFilePath(string kind, int entityId)
    {
        try
        {
            return Directory.EnumerateFiles(
                    CardBannersDirectory,
                    $"{SanitizeKind(kind)}-{entityId.ToString(CultureInfo.InvariantCulture)}.*")
                .FirstOrDefault();
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public async Task<string> SaveCardBannerFileAsync(
        string kind,
        int entityId,
        Stream content,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        string safeKind = SanitizeKind(kind);
        DeleteCardBannerFile(safeKind, entityId);

        string ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrEmpty(ext) || ext.Length > 8)
        {
            ext = ".jpg";
        }

        ext = ext.ToLowerInvariant();
        string filename = $"{safeKind}-{entityId.ToString(CultureInfo.InvariantCulture)}{ext}";
        string fullPath = Path.Combine(CardBannersDirectory, filename);

        await using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(fs, cancellationToken);
        }

        return filename;
    }

    private static string SanitizeKind(string kind)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(kind.Where(c => c != '.' && !invalid.Contains(c)).ToArray());
    }
}