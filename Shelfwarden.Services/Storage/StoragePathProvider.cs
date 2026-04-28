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
    }

    public string CoversDirectory { get; }

    public string AuthorPhotosDirectory { get; }

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
}