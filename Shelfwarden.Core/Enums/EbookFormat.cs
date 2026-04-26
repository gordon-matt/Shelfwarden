namespace Shelfwarden.Enums;

/// <summary>
/// Supported ebook file formats. v1 ships with EPUB + PDF.
/// </summary>
public enum EbookFormat
{
    Unknown = 0,
    Epub = 1,
    Pdf = 2,
}

public static class EbookFormatExtensions
{
    public static EbookFormat FromExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return EbookFormat.Unknown;
        }

        string ext = System.IO.Path.GetExtension(path);
        return ext.ToLowerInvariant() switch
        {
            ".epub" => EbookFormat.Epub,
            ".pdf" => EbookFormat.Pdf,
            _ => EbookFormat.Unknown,
        };
    }

    public static string ToContentType(this EbookFormat format) => format switch
    {
        EbookFormat.Epub => "application/epub+zip",
        EbookFormat.Pdf => "application/pdf",
        _ => "application/octet-stream",
    };

    public static IReadOnlyList<string> SupportedExtensions { get; } = [".epub", ".pdf"];
}
