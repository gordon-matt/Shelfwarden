using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using VersOne.Epub;
using VersOne.Epub.Options;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Streams the body text of an EPUB file using <c>VersOne.Epub</c>. The reading-order HTML
/// of each chapter is fetched lazily, stripped of tags, and yielded paragraph-by-paragraph so
/// the TTS pipeline never needs to hold the whole book in memory.
/// </summary>
public sealed partial class EpubTextExtractor(ILogger<EpubTextExtractor> logger) : IBookTextExtractor
{
    private static readonly EpubReaderOptions ReaderOptions = new()
    {
        PackageReaderOptions = new PackageReaderOptions
        {
            IgnoreMissingToc = true,
            SkipInvalidManifestItems = true,
        },
        XmlReaderOptions = new XmlReaderOptions { SkipXmlHeaders = true },
    };

    public EbookFormat Format => EbookFormat.Epub;

    public async IAsyncEnumerable<string> ExtractAsync(
        string filePath,
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

        if (bookRef is null)
        {
            yield break;
        }

        try
        {
            var readingOrder = await bookRef.GetReadingOrderAsync();
            foreach (var chapter in readingOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string html;
                try
                {
                    html = await chapter.ReadContentAsTextAsync();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Skipping unreadable EPUB chapter {Path} in {File}", chapter.FilePath, filePath);
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
            bookRef.Dispose();
        }
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

        // Drop script / style blocks entirely — they're never user-facing text.
        string cleaned = ScriptOrStyleRegex().Replace(html, " ");

        // Convert block-level boundaries to paragraph separators so we can keep paragraph
        // structure when we strip the rest of the tags. This keeps headings, list items
        // and table cells from running together.
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
