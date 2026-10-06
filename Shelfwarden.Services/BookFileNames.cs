namespace Shelfwarden.Services;

public static class BookFileNames
{
    // Path.GetInvalidFileNameChars is platform-specific; strip the Windows set everywhere so a name
    // generated on a Linux server still saves cleanly on a Windows client.
    private static readonly HashSet<char> InvalidChars =
        [.. Path.GetInvalidFileNameChars(), '<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>
    /// Friendly file name for a downloaded book: the title with filesystem-illegal characters
    /// removed, plus the file's own extension (or one implied by the format).
    /// </summary>
    public static string BuildDownloadFileName(int bookId, string? title, string filePath, EbookFormat format)
    {
        string ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
        {
            ext = format switch
            {
                EbookFormat.Epub => ".epub",
                EbookFormat.Pdf => ".pdf",
                _ => string.Empty,
            };
        }

        string baseName = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(filePath)
            : title;

        string sanitised = new string([.. baseName.Where(c => !char.IsControl(c) && !InvalidChars.Contains(c))])
            .Trim()
            .TrimEnd('.');

        if (string.IsNullOrEmpty(sanitised))
        {
            sanitised = $"book-{bookId}";
        }

        return sanitised + ext;
    }
}
