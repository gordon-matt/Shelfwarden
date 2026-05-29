using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Shared HTML-to-plain-text helpers for EPUB chapter content. Kept separate from
/// <see cref="EpubSectionParser"/> so the stripping rules can be unit-tested without
/// opening real EPUB files.
/// </summary>
public static partial class EpubHtmlTextExtractor
{
    public static IEnumerable<string> ExtractParagraphs(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            yield break;
        }

        // Drop the entire document head — it holds <title> text that is almost never meant to be
        // read aloud (Calibre often copies internal filenames like "c1X" into <title>).
        string cleaned = HeadRegex().Replace(html, " ");
        cleaned = ScriptOrStyleRegex().Replace(cleaned, " ");
        cleaned = HtmlCommentRegex().Replace(cleaned, " ");
        cleaned = BlockBoundaryRegex().Replace(cleaned, "\n\n");
        cleaned = LineBreakRegex().Replace(cleaned, "\n");

        string textOnly = TagRegex().Replace(cleaned, string.Empty);
        textOnly = WebUtility.HtmlDecode(textOnly);

        var sb = new StringBuilder();
        foreach (string raw in textOnly.Split('\n'))
        {
            string trimmed = WhitespaceRegex().Replace(raw, " ").Trim();
            if (trimmed.Length == 0)
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }
            sb.Append(trimmed);
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    [GeneratedRegex(@"<head[^>]*>.*?</head>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HeadRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyleRegex();

    [GeneratedRegex(@"</?(p|div|h[1-6]|li|tr|td|th|article|section|blockquote|pre)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockBoundaryRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
