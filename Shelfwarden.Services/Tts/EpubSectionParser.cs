using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using VersOne.Epub;
using VersOne.Epub.Options;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Parses an EPUB into sections using its navigation (table of contents) and streams section
/// text via <c>VersOne.Epub</c>. EPUBs carry structured navigation, so detection is reliable:
/// each top-level nav entry becomes a section spanning the reading-order documents from it up to
/// the next entry. Reading-order documents that precede the first nav entry (cover / title page)
/// are grouped into a leading "Front matter" section that the classifier excludes by default.
/// </summary>
public sealed partial class EpubSectionParser(ILogger<EpubSectionParser> logger) : IEbookSectionParser
{
    private static readonly EpubReaderOptions ReaderOptions = new()
    {
        PackageReaderOptions = new PackageReaderOptions
        {
            IgnoreMissingToc = true,
            SkipInvalidManifestItems = true,
        },
        XmlReaderOptions = new XmlReaderOptions { SkipXmlHeaders = true },
        NavigationReaderOptions = new NavigationReaderOptions(EpubReaderOptionsPreset.RELAXED),
    };

    public EbookFormat Format => EbookFormat.Epub;

    public async Task<SectionDetectionResult> ParseSectionsAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        EpubBookRef? bookRef = null;
        try
        {
            bookRef = await EpubReader.OpenBookAsync(filePath, ReaderOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to open EPUB {FilePath} for section parsing", filePath);
        }

        if (bookRef is null)
        {
            return new SectionDetectionResult([], SectionDetectionQuality.None, "Could not read this EPUB.");
        }

        try
        {
            var readingOrder = await bookRef.GetReadingOrderAsync() ?? [];
            int docCount = readingOrder.Count;
            if (docCount == 0)
            {
                return new SectionDetectionResult([], SectionDetectionQuality.None, "This EPUB has no readable content.");
            }

            // FilePath → reading-order index, so nav links can be mapped to spine positions.
            var indexByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < docCount; i++)
            {
                indexByPath.TryAdd(readingOrder[i].FilePath, i);
            }

            List<EpubNavigationItemRef> navigation;
            try
            {
                navigation = await bookRef.GetNavigationAsync() ?? [];
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "EPUB {FilePath} navigation unreadable; falling back to per-document sections", filePath);
                navigation = [];
            }

            var navEntries = new List<(string Title, int Index)>();
            CollectNavEntries(navigation, indexByPath, navEntries);

            // Keep the first title seen per spine index (top-most nav level), ordered by spine position.
            var orderedEntries = navEntries
                .GroupBy(e => e.Index)
                .Select(g => (Title: g.First().Title, Index: g.Key))
                .OrderBy(e => e.Index)
                .ToList();

            return orderedEntries.Count == 0
                ? BuildPerDocumentSections(docCount)
                : BuildSectionsFromNavigation(orderedEntries, docCount);
        }
        finally
        {
            bookRef?.Dispose();
        }
    }

    private static SectionDetectionResult BuildSectionsFromNavigation(
        List<(string Title, int Index)> orderedEntries,
        int docCount)
    {
        var sections = new List<BookSection>();

        // Reading-order documents before the first nav entry (cover / title / copyright that the
        // ToC skips) become a leading section so they can be explicitly excluded.
        if (orderedEntries[0].Index > 0)
        {
            sections.Add(new BookSection
            {
                Title = "Front matter",
                ReadingOrderIndices = Range(0, orderedEntries[0].Index - 1),
            });
        }

        for (int i = 0; i < orderedEntries.Count; i++)
        {
            int start = orderedEntries[i].Index;
            int end = i + 1 < orderedEntries.Count ? orderedEntries[i + 1].Index - 1 : docCount - 1;
            if (end < start)
            {
                // Two nav entries point at the same document (anchors within one file). Fold the
                // later title into the same document rather than emitting an empty section.
                continue;
            }

            sections.Add(new BookSection
            {
                Title = string.IsNullOrWhiteSpace(orderedEntries[i].Title) ? $"Section {i + 1}" : orderedEntries[i].Title.Trim(),
                ReadingOrderIndices = Range(start, end),
            });
        }

        SectionClassifier.ApplyDefaults(sections);
        return new SectionDetectionResult(sections, SectionDetectionQuality.Structured, null);
    }

    private static SectionDetectionResult BuildPerDocumentSections(int docCount)
    {
        var sections = new List<BookSection>(docCount);
        for (int i = 0; i < docCount; i++)
        {
            sections.Add(new BookSection
            {
                Title = $"Section {i + 1}",
                ReadingOrderIndices = [i],
            });
        }

        SectionClassifier.ApplyDefaults(sections);
        return new SectionDetectionResult(
            sections,
            SectionDetectionQuality.Heuristic,
            "This EPUB has no table of contents, so each internal document is shown as its own section. Review the selection before generating.");
    }

    private static void CollectNavEntries(
        IEnumerable<EpubNavigationItemRef> items,
        IReadOnlyDictionary<string, int> indexByPath,
        List<(string Title, int Index)> result)
    {
        foreach (var item in items)
        {
            string? path = item.Link?.ContentFilePath ?? item.HtmlContentFileRef?.FilePath;
            if (!string.IsNullOrEmpty(path))
            {
                string clean = StripAnchor(path);
                if (indexByPath.TryGetValue(clean, out int index))
                {
                    result.Add((item.Title ?? string.Empty, index));
                }
            }

            if (item.NestedItems is { Count: > 0 })
            {
                CollectNavEntries(item.NestedItems, indexByPath, result);
            }
        }
    }

    public async IAsyncEnumerable<string> ExtractAsync(
        string filePath,
        IReadOnlyList<BookSection>? sections,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EpubBookRef? bookRef = null;
        try
        {
            bookRef = await EpubReader.OpenBookAsync(filePath, ReaderOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to open EPUB {FilePath} for TTS extraction", filePath);
            yield break;
        }

        try
        {
            var readingOrder = await bookRef.GetReadingOrderAsync() ?? [];
            IEnumerable<int> indices = sections is { Count: > 0 }
                ? sections.SelectMany(s => s.ReadingOrderIndices)
                : Enumerable.Range(0, readingOrder.Count);

            foreach (int index in indices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (index < 0 || index >= readingOrder.Count)
                {
                    continue;
                }

                string html;
                try
                {
                    html = await readingOrder[index].ReadContentAsTextAsync();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Skipping unreadable EPUB document {Index} in {File}", index, filePath);
                    continue;
                }

                foreach (string paragraph in ExtractParagraphs(html))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(paragraph))
                    {
                        yield return paragraph;
                    }
                }
            }
        }
        finally
        {
            bookRef?.Dispose();
        }
    }

    private static List<int> Range(int startInclusive, int endInclusive)
    {
        var list = new List<int>(Math.Max(0, endInclusive - startInclusive + 1));
        for (int i = startInclusive; i <= endInclusive; i++)
        {
            list.Add(i);
        }
        return list;
    }

    private static string StripAnchor(string path)
    {
        int hash = path.IndexOf('#', StringComparison.Ordinal);
        return hash >= 0 ? path[..hash] : path;
    }

    /// <summary>
    /// Crude but robust paragraph-level HTML to text. We deliberately avoid pulling in a full
    /// HTML parser: the input is mostly XHTML, the output only feeds a TTS engine, and the
    /// alternatives (HtmlAgilityPack, AngleSharp) would balloon the dependency surface.
    /// </summary>
    private static IEnumerable<string> ExtractParagraphs(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            yield break;
        }

        string cleaned = ScriptOrStyleRegex().Replace(html, " ");
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
