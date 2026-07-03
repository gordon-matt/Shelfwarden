using UglyToad.PdfPig.Content;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Detects and removes running heads / footers by page <em>geometry</em> rather than by repeated text.
/// The statistical <see cref="PdfMarginFilter"/> only catches chrome whose text is constant (a fixed
/// book title, a folded page number). Many books, however, alternate a fixed title on verso pages with
/// a <em>changing chapter / part title</em> on recto pages, and scanned (OCR) books frequently place
/// the head, the folio and the first body line so close together that a generic text extractor fuses
/// them into a single line. Neither case can be caught statistically.
///
/// This detector groups a page's words into visual lines by vertical <em>overlap</em> (which is
/// independent of the odd, inflated font metrics OCR layers report), then treats the top-most line as a
/// running head — and the bottom-most as a footer — when it is markedly shorter than the body lines and
/// sits in the page margin. It can then rebuild the page's spoken text with those lines omitted, no
/// matter what they say.
/// </summary>
public static class PdfRunningHeadDetector
{
    /// <summary>A header line must sit above this fraction of the page height (PDF origin is bottom-left).</summary>
    private const double HeaderBandFraction = 0.80;

    /// <summary>A footer line must sit below this fraction of the page height.</summary>
    private const double FooterBandFraction = 0.20;

    /// <summary>A running head / footer is short in absolute terms — a longer top/bottom line is body text.</summary>
    private const int MaxMarginWords = 8;

    /// <summary>Need a body block to judge what "short" means for this page.</summary>
    private const int MinLinesForAnalysis = 3;

    /// <summary>A single word line whose vertical extent overlaps by more than this is the same visual line.</summary>
    private const double OverlapEpsilon = 0.1;

    /// <summary>Does this page begin with a line that looks like a running head?</summary>
    public static bool HasHeader(Page page)
    {
        var lines = BuildLines(page);
        return lines.Count >= MinLinesForAnalysis && IsHeader(lines, page.Height);
    }

    /// <summary>Does this page end with a line that looks like a running footer (e.g. a page number)?</summary>
    public static bool HasFooter(Page page)
    {
        var lines = BuildLines(page);
        return lines.Count >= MinLinesForAnalysis && IsFooter(lines, page.Height);
    }

    /// <summary>
    /// Rebuilds a page's text in reading order (top-to-bottom, left-to-right) from its words, dropping
    /// the running head and/or footer line when permitted and present. Used instead of the generic
    /// content-order extractor on documents that carry running heads, because it is the only way to
    /// reliably peel off a head that the extractor would otherwise splice into the first body line.
    /// Returns null when the page has too little text to analyse, so the caller can fall back.
    /// </summary>
    public static string? BuildBodyText(Page page, bool allowDropHeader, bool allowDropFooter)
    {
        var lines = BuildLines(page);
        if (lines.Count == 0)
        {
            return null;
        }

        int start = 0;
        int end = lines.Count - 1;

        if (lines.Count >= MinLinesForAnalysis)
        {
            if (allowDropHeader && IsHeader(lines, page.Height))
            {
                start++;
            }

            if (allowDropFooter && end > start && IsFooter(lines, page.Height))
            {
                end--;
            }
        }

        if (start > end)
        {
            return string.Empty;
        }

        return string.Join('\n', lines.GetRange(start, end - start + 1).Select(l => l.Text));
    }

    private static bool IsHeader(IReadOnlyList<TextLine> lines, double pageHeight)
    {
        var top = lines[0];
        return top.Top >= pageHeight * HeaderBandFraction
            && IsShort(top.WordCount, MedianBodyWords(lines));
    }

    private static bool IsFooter(IReadOnlyList<TextLine> lines, double pageHeight)
    {
        var bottom = lines[^1];
        return bottom.Top <= pageHeight * FooterBandFraction
            && IsShort(bottom.WordCount, MedianBodyWords(lines));
    }

    /// <summary>A margin line is at most a handful of words and no more than half the length of a body line.</summary>
    private static bool IsShort(int wordCount, double medianBodyWords)
        => wordCount > 0
            && wordCount <= MaxMarginWords
            && wordCount * 2 <= medianBodyWords;

    private static double MedianBodyWords(IReadOnlyList<TextLine> lines)
    {
        // Ignore the first and last lines — the very lines we're deciding about — so the "typical body
        // line length" isn't dragged down by the short chrome we're trying to detect.
        var counts = new List<int>(lines.Count);
        for (int i = 1; i < lines.Count - 1; i++)
        {
            counts.Add(lines[i].WordCount);
        }

        if (counts.Count == 0)
        {
            return 0;
        }

        counts.Sort();
        return counts[counts.Count / 2];
    }

    private static List<TextLine> BuildLines(Page page)
    {
        List<Word> words;
        try
        {
            words = page.GetWords()
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }

        if (words.Count == 0)
        {
            return [];
        }

        var ordered = words.OrderByDescending(w => w.BoundingBox.Top).ToList();

        var groups = new List<List<Word>>();
        double lineTop = double.NaN;
        double lineBottom = double.NaN;

        foreach (var word in ordered)
        {
            double wTop = word.BoundingBox.Top;
            double wBottom = word.BoundingBox.Bottom;

            // Two words share a visual line when their vertical spans overlap. This is robust to the
            // inflated / inconsistent font sizes OCR layers report, where a fixed tolerance fails.
            bool sameLine = groups.Count > 0
                && Math.Min(wTop, lineTop) - Math.Max(wBottom, lineBottom) > OverlapEpsilon;

            if (!sameLine)
            {
                groups.Add([]);
                lineTop = wTop;
                lineBottom = wBottom;
            }
            else
            {
                lineTop = Math.Max(lineTop, wTop);
                lineBottom = Math.Min(lineBottom, wBottom);
            }

            groups[^1].Add(word);
        }

        return groups.ConvertAll(ToTextLine);
    }

    private static TextLine ToTextLine(List<Word> group)
    {
        string text = string.Join(' ', group
            .OrderBy(w => w.BoundingBox.Left)
            .Select(w => w.Text)).Trim();

        return new TextLine(
            Top: group.Max(w => w.BoundingBox.Top),
            WordCount: group.Count,
            Text: text);
    }

    private readonly record struct TextLine(double Top, int WordCount, string Text);
}
