using System.Text;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Text that repeats in the top or bottom margin of many pages — the "running head" (often the book
/// title / chapter title) and "running footer" (often a page number). PdfPig has no notion of these,
/// so we detect them ourselves and strip them from the spoken text.
/// </summary>
public sealed record PdfMarginPatterns(IReadOnlySet<string> Headers, IReadOnlySet<string> Footers)
{
    public static readonly PdfMarginPatterns Empty =
        new(new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));

    public bool IsEmpty => Headers.Count == 0 && Footers.Count == 0;
}

/// <summary>
/// Detects and removes running heads / footers (and bare page-number "folios") from PDF page text.
/// PdfPig — like every raw PDF extractor — emits the header, body and footer as ordinary lines, so a
/// TTS pass will happily read "MARBLEHEAD 15" before every chapter page. There is no reliable
/// structural marker for a header in the PDF spec, so we infer it statistically: a line that appears
/// in the same normalized form near the top (or bottom) of a large fraction of pages is a running
/// head (or footer). Page numbers vary per page, but collapse to a single normalized form ("#") once
/// digits are folded, so they're caught the same way. Pure and stateless so it is unit-testable.
/// </summary>
public static class PdfMarginFilter
{
    /// <summary>Only the first / last few lines of a page can be a running head / footer.</summary>
    private const int MaxMarginLines = 2;

    /// <summary>Repeated candidate longer than this is treated as body text, never a header.</summary>
    private const int MaxMarginalLength = 100;

    /// <summary>
    /// Builds the running-head / running-footer patterns from a sample of page texts. A candidate is
    /// accepted once it recurs on at least a quarter of the sampled pages (and at least three), which
    /// is enough to separate genuine repeating chrome from the occasional coincidental line.
    /// </summary>
    public static PdfMarginPatterns Detect(IReadOnlyList<string>? pageTexts)
    {
        if (pageTexts is not { Count: > 0 })
        {
            return PdfMarginPatterns.Empty;
        }

        var headerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var footerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        int usablePages = 0;

        foreach (string pageText in pageTexts)
        {
            var lines = NonEmptyLines(pageText);
            if (lines.Count == 0)
            {
                continue;
            }

            usablePages++;

            int top = Math.Min(MaxMarginLines, lines.Count);
            for (int i = 0; i < top; i++)
            {
                Bump(headerCounts, NormalizeForMatch(lines[i]));
            }

            int bottom = Math.Min(MaxMarginLines, lines.Count);
            for (int i = 0; i < bottom; i++)
            {
                Bump(footerCounts, NormalizeForMatch(lines[lines.Count - 1 - i]));
            }
        }

        if (usablePages < 3)
        {
            return PdfMarginPatterns.Empty;
        }

        int minOccurrences = Math.Max(3, (int)Math.Ceiling(usablePages * 0.25));

        return new PdfMarginPatterns(
            SelectPatterns(headerCounts, minOccurrences),
            SelectPatterns(footerCounts, minOccurrences));
    }

    /// <summary>
    /// Removes any detected running head / footer lines and bare page numbers from the top and bottom
    /// of a single page's raw extracted text. Must run <em>before</em> <see cref="PdfTextNormalizer"/>
    /// re-flows the page, since the normalizer collapses these lines into the surrounding paragraph.
    /// </summary>
    public static string StripMarginals(string? pageText, PdfMarginPatterns patterns)
    {
        if (string.IsNullOrEmpty(pageText))
        {
            return string.Empty;
        }

        var lines = pageText
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();

        RemoveMatchingFromStart(lines, patterns.Headers);
        RemoveMatchingFromEnd(lines, patterns.Footers);

        return string.Join('\n', lines);
    }

    private static void RemoveMatchingFromStart(List<string> lines, IReadOnlySet<string> headers)
    {
        int removed = 0;
        while (removed < MaxMarginLines)
        {
            int idx = 0;
            while (idx < lines.Count && lines[idx].Trim().Length == 0)
            {
                idx++;
            }

            if (idx >= lines.Count || !IsMarginal(lines[idx].Trim(), headers))
            {
                break;
            }

            lines.RemoveRange(0, idx + 1);
            removed++;
        }
    }

    private static void RemoveMatchingFromEnd(List<string> lines, IReadOnlySet<string> footers)
    {
        int removed = 0;
        while (removed < MaxMarginLines)
        {
            int idx = lines.Count - 1;
            while (idx >= 0 && lines[idx].Trim().Length == 0)
            {
                idx--;
            }

            if (idx < 0 || !IsMarginal(lines[idx].Trim(), footers))
            {
                break;
            }

            lines.RemoveRange(idx, lines.Count - idx);
            removed++;
        }
    }

    private static bool IsMarginal(string rawTrimmed, IReadOnlySet<string> patterns)
        => IsBareFolio(rawTrimmed) || patterns.Contains(NormalizeForMatch(rawTrimmed));

    private static IReadOnlySet<string> SelectPatterns(Dictionary<string, int> counts, int minOccurrences)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (pattern, count) in counts)
        {
            // "#" is the folded page number and is always worth stripping; otherwise require at least
            // two characters so a stray single letter ("I", "a") repeated near a margin isn't treated
            // as a running head.
            bool acceptableShape = pattern == "#"
                || (pattern.Length is >= 2 and <= MaxMarginalLength);

            if (count >= minOccurrences && acceptableShape)
            {
                set.Add(pattern);
            }
        }

        return set;
    }

    private static void Bump(Dictionary<string, int> counts, string key)
    {
        if (key.Length == 0)
        {
            return;
        }

        counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    private static List<string> NonEmptyLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    /// <summary>
    /// A line that is nothing but a page number — either Arabic ("15") or Roman ("xiv"). Single-letter
    /// Roman numerals are excluded so a solitary "I" (the pronoun) or "C" isn't mistaken for a folio.
    /// </summary>
    private static bool IsBareFolio(string rawTrimmed)
    {
        if (rawTrimmed.Length is 0 or > 8)
        {
            return false;
        }

        bool allDigits = true;
        bool allRoman = rawTrimmed.Length >= 2;
        foreach (char c in rawTrimmed)
        {
            if (!char.IsDigit(c))
            {
                allDigits = false;
            }

            if ("ivxlcdmIVXLCDM".IndexOf(c) < 0)
            {
                allRoman = false;
            }
        }

        return allDigits || allRoman;
    }

    /// <summary>
    /// Folds a line to a comparison key: lower-cased letters, runs of digits collapsed to a single
    /// '#', and all punctuation reduced to single spaces. So "MARBLEHEAD • 15" and "MARBLEHEAD · 16"
    /// both become "marblehead #", letting a per-page page number match a single stored pattern.
    /// </summary>
    internal static string NormalizeForMatch(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(line.Length);
        bool lastWasDigit = false;
        foreach (char c in line.Trim())
        {
            if (char.IsLetter(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasDigit = false;
            }
            else if (char.IsDigit(c))
            {
                if (!lastWasDigit)
                {
                    sb.Append('#');
                }
                lastWasDigit = true;
            }
            else
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                {
                    sb.Append(' ');
                }
                lastWasDigit = false;
            }
        }

        return sb.ToString().Trim();
    }
}
