using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Shelfwarden.Services.Metadata;

/// <summary>HTTP constants shared by the online metadata providers.</summary>
internal static class MetadataHttp
{
    /// <summary>
    /// Descriptive User-Agent. Open Library explicitly asks callers to identify themselves, and a
    /// real UA avoids bot-blocking on the public APIs.
    /// </summary>
    public const string UserAgent = "Shelfwarden/1.0 (+https://github.com/Shelfwarden)";
}

/// <summary>Small parsing/cleanup helpers shared by the online metadata providers.</summary>
internal static partial class MetadataNormalization
{
    public static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Strips ISBN formatting (hyphens/spaces) and upper-cases a trailing check digit 'X'.</summary>
    public static string NormalizeIsbn(string isbn)
    {
        if (string.IsNullOrWhiteSpace(isbn))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(isbn.Length);
        foreach (char c in isbn)
        {
            if (char.IsDigit(c))
            {
                sb.Append(c);
            }
            else if (c is 'x' or 'X')
            {
                sb.Append('X');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts provider HTML descriptions to plain text: drops tags, collapses whitespace and
    /// decodes entities. The book editor renders descriptions as HTML, but online sources mix
    /// real markup with escaped text, so normalising to clean text is the safest default.
    /// </summary>
    public static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        string withoutTags = HtmlTagRegex().Replace(html, " ");
        string decoded = WebUtility.HtmlDecode(withoutTags);
        string collapsed = WhitespaceRegex().Replace(decoded, " ").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    /// <summary>Parses the loose date formats online sources emit: <c>YYYY</c>, <c>YYYY-MM</c>, <c>YYYY-MM-DD</c>.</summary>
    public static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string value = raw.Trim();
        string[] formats = ["yyyy-MM-dd", "yyyy-MM", "yyyy", "yyyy/MM/dd", "MMMM d, yyyy", "d MMMM yyyy"];
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exact))
        {
            return exact;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        // Last resort: a bare 4-digit year embedded in a longer string.
        var match = YearRegex().Match(value);
        return match.Success && int.TryParse(match.Value, out int year)
            ? new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            : null;
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\b(1[5-9]\d{2}|20\d{2})\b", RegexOptions.Compiled)]
    private static partial Regex YearRegex();
}
