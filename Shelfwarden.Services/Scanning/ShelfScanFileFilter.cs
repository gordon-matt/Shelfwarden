namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Decides whether a file path should be considered when scanning shelf folders for ebooks.
/// </summary>
internal static class ShelfScanFileFilter
{
    /// <summary>Directory name segments; any file under these paths is skipped.</summary>
    private static readonly HashSet<string> IgnoredPathSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        // Calibre trash / notes sidecars (not real library books)
        ".caltrash",
        ".calnotes",
    };

    public static bool ShouldInclude(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
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
