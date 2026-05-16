namespace Shelfwarden.Services;

/// <summary>
/// Decides whether a file path should be included when scanning or registering extra content.
/// Excludes OS / NAS metadata — not user-facing documents (including PDFs and ebooks).
/// </summary>
internal static class ExtrasScanFileFilter
{
    private static readonly HashSet<string> IgnoredFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows
        "Thumbs.db",
        "ehthumbs.db",
        "ehthumbs_vista.db",
        "desktop.ini",
        "Desktop.ini",

        // macOS
        ".DS_Store",
        ".localized",

        // Linux / KDE
        ".directory",

        // Synology (files)
        ".@__thumb",
        ".synologyworkingdirectory",
    };

    /// <summary>Directory name segments; any file under these paths is skipped.</summary>
    private static readonly HashSet<string> IgnoredPathSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        // Synology NAS
        "@eadir",
        "@EADIR",
        "#recycle",
        "@sharebin",
        "@tmp",
        "@SynoResource",
        "@syno",
        ".@__thumb",
        ".synologyworkingdirectory",

        // macOS
        "__MACOSX",
        ".Trashes",
        ".Spotlight-V100",
        ".fseventsd",
        ".TemporaryItems",
        ".AppleDouble",
        ".DocumentRevisions-V100",
        ".LSOverride",
        ".metadata_never_index",

        // Windows (often on network shares)
        "$Recycle.Bin",
        "System Volume Information",

        // Linux
        "lost+found",
        ".Trash",
        ".Trash-1000",
    };

    public static bool ShouldInclude(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        string fileName = Path.GetFileName(fullPath);
        if (fileName.Length == 0)
        {
            return false;
        }

        if (IgnoredFileNames.Contains(fileName))
        {
            return false;
        }

        // macOS AppleDouble resource forks: "._MyFile.pdf"
        if (fileName.StartsWith("._", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (string segment in EnumeratePathSegments(fullPath))
        {
            if (IgnoredPathSegments.Contains(segment))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> EnumeratePathSegments(string fullPath)
    {
        string? dir = Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrEmpty(dir))
        {
            yield return Path.GetFileName(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }
}
