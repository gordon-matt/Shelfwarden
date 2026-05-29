using System.Runtime.CompilerServices;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Outline;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Parses a PDF into sections using a tiered strategy, since PDFs carry no guaranteed structure:
/// <list type="number">
///   <item>the bookmark / outline tree (reliable when present);</item>
///   <item>heading heuristics — large text near the top of a page that reads like a chapter title;</item>
///   <item>a single catch-all section spanning the whole document (manual review suggested).</item>
/// </list>
/// Text is streamed page-by-page with <c>UglyToad.PdfPig</c>'s content-order extractor.
/// </summary>
public sealed class PdfSectionParser(ILogger<PdfSectionParser> logger) : IEbookSectionParser
{
    /// <summary>Headings are at least this much larger than body text.</summary>
    private const double HeadingFontRatio = 1.3;

    /// <summary>Headings sit in roughly the top third of the page (PDF origin is bottom-left).</summary>
    private const double HeadingTopFraction = 0.62;

    public EbookFormat Format => EbookFormat.Pdf;

    public Task<SectionDetectionResult> ParseSectionsAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(filePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to open PDF {FilePath} for section parsing", filePath);
            return Task.FromResult(new SectionDetectionResult([], SectionDetectionQuality.None, "Could not read this PDF."));
        }

        try
        {
            int pageCount = document.NumberOfPages;
            if (pageCount == 0)
            {
                return Task.FromResult(new SectionDetectionResult([], SectionDetectionQuality.None, "This PDF has no pages."));
            }

            var fromBookmarks = ParseFromBookmarks(document, pageCount);
            if (fromBookmarks.Count >= 2)
            {
                SectionClassifier.ApplyDefaults(fromBookmarks);
                return Task.FromResult(new SectionDetectionResult(fromBookmarks, SectionDetectionQuality.Structured, null));
            }

            var fromHeadings = ParseFromHeuristics(document, pageCount, cancellationToken);
            if (fromHeadings.Count >= 2)
            {
                SectionClassifier.ApplyDefaults(fromHeadings);
                return Task.FromResult(new SectionDetectionResult(
                    fromHeadings,
                    SectionDetectionQuality.Heuristic,
                    "No bookmarks found — chapters were guessed from the page layout. Please double-check the selection before generating."));
            }

            // Fallback: a single section covering the whole document.
            var single = new List<BookSection>
            {
                new() { Title = "Whole document", StartPage = 1, EndPage = pageCount, Kind = SectionKind.Chapter },
            };
            return Task.FromResult(new SectionDetectionResult(
                single,
                SectionDetectionQuality.None,
                "This PDF has no bookmarks and no detectable chapter headings, so the whole document is treated as one part."));
        }
        finally
        {
            document.Dispose();
        }
    }

    private List<BookSection> ParseFromBookmarks(PdfDocument document, int pageCount)
    {
        if (!document.TryGetBookmarks(out var bookmarks) || bookmarks is null)
        {
            return [];
        }

        var entries = bookmarks.GetNodes()
            .OfType<DocumentBookmarkNode>()
            .Where(n => n.PageNumber >= 1 && n.PageNumber <= pageCount)
            .Select(n => (Title: n.Title?.Trim() ?? string.Empty, Page: n.PageNumber))
            .GroupBy(e => e.Page)
            .Select(g => (g.First().Title, Page: g.Key))
            .OrderBy(e => e.Page)
            .ToList();

        if (entries.Count == 0)
        {
            return [];
        }

        var sections = new List<BookSection>();

        // Pages before the first bookmark are grouped as a leading section (often the cover /
        // title / copyright) so they can be excluded.
        if (entries[0].Page > 1)
        {
            sections.Add(new BookSection { Title = "Front matter", StartPage = 1, EndPage = entries[0].Page - 1 });
        }

        for (int i = 0; i < entries.Count; i++)
        {
            int start = entries[i].Page;
            int end = i + 1 < entries.Count ? entries[i + 1].Page - 1 : pageCount;
            if (end < start)
            {
                continue;
            }

            sections.Add(new BookSection
            {
                Title = string.IsNullOrWhiteSpace(entries[i].Title) ? $"Section {i + 1}" : entries[i].Title,
                StartPage = start,
                EndPage = end,
            });
        }

        return sections;
    }

    private List<BookSection> ParseFromHeuristics(PdfDocument document, int pageCount, CancellationToken cancellationToken)
    {
        double bodyFontSize = EstimateBodyFontSize(document, pageCount, cancellationToken);
        var starts = new List<(string Title, int Page)>();

        for (int pageNum = 1; pageNum <= pageCount; pageNum++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Page page;
            try
            {
                page = document.GetPage(pageNum);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Skipping unreadable PDF page {PageNumber} during heading detection", pageNum);
                continue;
            }

            string? heading = TryGetHeading(page, bodyFontSize);
            if (heading is not null && SectionClassifier.LooksLikeChapterHeading(heading))
            {
                starts.Add((heading, pageNum));
            }
        }

        if (starts.Count == 0)
        {
            return [];
        }

        var sections = new List<BookSection>();
        if (starts[0].Page > 1)
        {
            sections.Add(new BookSection { Title = "Front matter", StartPage = 1, EndPage = starts[0].Page - 1 });
        }

        for (int i = 0; i < starts.Count; i++)
        {
            int start = starts[i].Page;
            int end = i + 1 < starts.Count ? starts[i + 1].Page - 1 : pageCount;
            if (end < start)
            {
                continue;
            }

            sections.Add(new BookSection { Title = starts[i].Title, StartPage = start, EndPage = end });
        }

        return sections;
    }

    private static double EstimateBodyFontSize(PdfDocument document, int pageCount, CancellationToken cancellationToken)
    {
        // Sample up to ~30 evenly-spaced pages so the median isn't skewed by a title-heavy front
        // section, and so we don't pay for a full word extraction pass on huge documents.
        const int maxSamples = 30;
        int step = Math.Max(1, pageCount / maxSamples);
        var sizes = new List<double>();

        for (int pageNum = 1; pageNum <= pageCount; pageNum += step)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var word in document.GetPage(pageNum).GetWords())
                {
                    double size = WordFontSize(word);
                    if (size > 0)
                    {
                        sizes.Add(size);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _ = ex;
            }
        }

        if (sizes.Count == 0)
        {
            return 12d;
        }

        sizes.Sort();
        return sizes[sizes.Count / 2];
    }

    private static string? TryGetHeading(Page page, double bodyFontSize)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0)
        {
            return null;
        }

        double topY = words.Max(w => w.BoundingBox.Top);
        if (topY < page.Height * HeadingTopFraction)
        {
            return null;
        }

        // Words sharing the topmost text line (within a small vertical tolerance).
        double tolerance = Math.Max(2d, bodyFontSize * 0.6);
        var topLine = words
            .Where(w => topY - w.BoundingBox.Top <= tolerance)
            .OrderBy(w => w.BoundingBox.Left)
            .ToList();

        if (topLine.Count == 0)
        {
            return null;
        }

        double lineFont = topLine.Max(WordFontSize);
        if (lineFont < bodyFontSize * HeadingFontRatio)
        {
            return null;
        }

        string text = string.Join(' ', topLine.Select(w => w.Text)).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static double WordFontSize(Word word)
        => word.Letters.Count > 0 ? word.Letters.Max(l => l.FontSize) : word.BoundingBox.Height;

    public async IAsyncEnumerable<string> ExtractAsync(
        string filePath,
        IReadOnlyList<BookSection>? sections,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        PdfDocument? document = null;
        try
        {
            document = PdfDocument.Open(filePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to open PDF {FilePath} for TTS extraction", filePath);
            yield break;
        }

        try
        {
            int pageCount = document.NumberOfPages;
            foreach (var (start, end) in EnumeratePageRanges(sections, pageCount))
            {
                for (int pageNum = start; pageNum <= end; pageNum++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Page page;
                    try
                    {
                        page = document.GetPage(pageNum);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogDebug(ex, "Skipping unreadable PDF page {PageNumber} of {File}", pageNum, filePath);
                        continue;
                    }

                    string text;
                    try
                    {
                        text = ContentOrderTextExtractor.GetText(page) ?? string.Empty;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogDebug(ex, "Falling back to raw text on page {PageNumber} of {File}", pageNum, filePath);
                        text = page.Text ?? string.Empty;
                    }

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        yield return text;
                    }

                    // Keep very long PDFs cooperative with cancellation between pages.
                    await Task.Yield();
                }
            }
        }
        finally
        {
            document.Dispose();
        }
    }

    private static IEnumerable<(int Start, int End)> EnumeratePageRanges(
        IReadOnlyList<BookSection>? sections,
        int pageCount)
    {
        if (sections is not { Count: > 0 })
        {
            yield return (1, pageCount);
            yield break;
        }

        foreach (var section in sections)
        {
            int start = Math.Clamp(section.StartPage ?? 1, 1, pageCount);
            int end = Math.Clamp(section.EndPage ?? pageCount, start, pageCount);
            yield return (start, end);
        }
    }
}
