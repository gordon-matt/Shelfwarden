using System.Text;
using System.Xml;
using Shelfwarden.Services.Tts;

namespace Shelfwarden.Services.Opds;

/// <summary>
/// Makes metadata safe to put in an XML document. Scanned titles and descriptions occasionally
/// carry NULs and other control characters, which XML 1.0 forbids outright.
/// </summary>
public static class OpdsTextSanitizer
{
    /// <summary>Removes characters XML 1.0 can't represent, keeping valid surrogate pairs (emoji etc.).</summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (IsClean(value))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (XmlConvert.IsXmlChar(c))
            {
                sb.Append(c);
            }
            else if (i + 1 < value.Length && XmlConvert.IsXmlSurrogatePair(value[i + 1], c))
            {
                sb.Append(c).Append(value[i + 1]);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>Like <see cref="Clean"/>, but returns null for null / whitespace-only input.</summary>
    public static string? CleanOrNull(string? value)
    {
        string cleaned = Clean(value).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>Flattens an HTML book description to XML-safe plain text, one blank line between paragraphs.</summary>
    public static string? HtmlToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        string text = string.Join("\n\n", EpubHtmlTextExtractor.ExtractParagraphs(html));
        return CleanOrNull(text);
    }

    private static bool IsClean(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (!XmlConvert.IsXmlChar(value[i]))
            {
                return false;
            }
        }

        return true;
    }
}
