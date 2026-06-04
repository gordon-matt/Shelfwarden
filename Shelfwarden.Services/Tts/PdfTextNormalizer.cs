using System.Text;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Cleans up raw text extracted from a PDF page so it reads naturally when spoken.
/// PDF extractors emit one newline per <em>visual</em> line, which means a single word that
/// wraps across a line ends up split — often with a trailing hyphen ("clus-" / "tered").
/// Speaking those fragments separately ("clus", "tered") instead of the joined word
/// ("clustered") is the bug this fixes. Pure, stateless, and unit-testable.
/// </summary>
public static class PdfTextNormalizer
{
    /// <summary>
    /// Re-flows a block of PDF text: joins hyphenated line-wraps into single words, collapses
    /// soft line-wraps within a paragraph into spaces, and keeps blank-line paragraph breaks.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        // Drop the soft-hyphen (U+00AD) entirely — it is an invisible "you may break here" hint
        // that some PDFs leave embedded mid-word, and it confuses the tokenizer.
        string text = raw.Replace("\u00AD", string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        string[] lines = text.Split('\n');
        var sb = new StringBuilder(text.Length);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (line.Length == 0)
            {
                // Blank line → paragraph boundary. Avoid stacking multiple blank lines.
                if (sb.Length > 0 && sb[^1] != '\n')
                {
                    sb.Append('\n');
                }
                continue;
            }

            string? nextLine = FindNextNonEmptyLine(lines, i);

            if (nextLine is not null && TryJoinHyphenated(line, nextLine, out string joined))
            {
                // Hyphenated wrap: append without a trailing space so the next line's first
                // word fuses onto the stripped word.
                sb.Append(joined);
                continue;
            }

            sb.Append(line);

            // Soft wrap to a following line in the same paragraph → single space.
            if (nextLine is not null)
            {
                sb.Append(' ');
            }
        }

        return sb.ToString().Trim();
    }

    private static string? FindNextNonEmptyLine(string[] lines, int currentIndex)
    {
        int next = currentIndex + 1;
        if (next >= lines.Length)
        {
            return null;
        }

        // Only the immediately following line continues the current one. A blank line there is a
        // paragraph break, so the current line has no continuation.
        string candidate = lines[next].Trim();
        return candidate.Length > 0 ? candidate : null;
    }

    /// <summary>
    /// If <paramref name="line"/> ends with a hyphenated word fragment, returns the line text
    /// with the word reattached to the start of <paramref name="nextLine"/>.
    /// </summary>
    private static bool TryJoinHyphenated(string line, string nextLine, out string joined)
    {
        joined = string.Empty;

        if (line.Length < 2 || line[^1] != '-')
        {
            return false;
        }

        // The character before the hyphen must be a letter (so we don't mangle "page 10 -").
        if (!char.IsLetter(line[^2]))
        {
            return false;
        }

        // The continuation must begin with a letter; otherwise the hyphen is likely meaningful
        // punctuation (e.g. a dash before a quote or number) rather than a wrapped word.
        char nextFirst = nextLine[0];
        if (!char.IsLetter(nextFirst))
        {
            return false;
        }

        // Lowercase continuation → almost certainly a wrapped single word ("clus-"+"tered"):
        // strip the hyphen. Uppercase continuation is usually a real compound that happened to
        // wrap ("Anglo-"+"Saxon"): keep the hyphen so we don't corrupt the spelling.
        joined = char.IsUpper(nextFirst) ? line : line[..^1];
        return true;
    }
}
