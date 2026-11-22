namespace Shelfwarden.Enums;

/// <summary>
/// Supported ebook file formats. v1 ships with EPUB + PDF.
/// </summary>
public enum EbookFormat
{
    /// <summary>The extension was missing or is not a supported ebook.</summary>
    Unknown = 0,

    /// <summary>An EPUB file.</summary>
    Epub = 1,

    /// <summary>A PDF file.</summary>
    Pdf = 2,
}

public static class EbookFormatExtensions
{
    /// <summary>The format for a file path, from its extension. Anything else is <see cref="EbookFormat.Unknown"/>.</summary>
    public static EbookFormat FromExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return EbookFormat.Unknown;
        }

        string ext = Path.GetExtension(path);
        return ext.ToLowerInvariant() switch
        {
            ".epub" => EbookFormat.Epub,
            ".pdf" => EbookFormat.Pdf,
            _ => EbookFormat.Unknown,
        };
    }

    /// <summary>The MIME type used when the file is downloaded.</summary>
    public static string ToContentType(this EbookFormat format) => format switch
    {
        EbookFormat.Epub => "application/epub+zip",
        EbookFormat.Pdf => "application/pdf",
        _ => "application/octet-stream",
    };

    /// <summary>Extensions the scanner accepts.</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } = [".epub", ".pdf"];
}