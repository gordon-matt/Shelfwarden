using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Shelfwarden.Models.Metadata;

namespace Shelfwarden.Services.Metadata;

/// <summary>
/// Metadata source that scrapes Amazon product pages, replicating Booklore's <c>AmazonBookParser</c>
/// (and Calibre's <c>amazon.py</c>) approach: run a keyword/ISBN search, collect the top ASINs, then
/// parse each <c>/dp/{asin}</c> detail page. Amazon has no public books API and actively rate-limits
/// scrapers, so this provider is best-effort — it swallows blocking (HTTP 503) and parse errors and
/// returns whatever it can rather than throwing.
/// </summary>
public sealed partial class AmazonMetadataProvider(
    ILogger<AmazonMetadataProvider> logger,
    IHttpClientFactory httpClientFactory) : IBookMetadataProvider
{
    private const string Domain = "com";
    private const string AcceptLanguage = "en-US,en;q=0.9";

    /// <summary>Cap on detail-page fetches per search to limit how hard we hammer Amazon.</summary>
    private const int MaxDetailFetches = 4;

    public string Name => "Amazon";

    public int Priority => 3;

    public async Task<IReadOnlyList<ExternalBookMetadataDto>> SearchAsync(BookMetadataQuery query, CancellationToken cancellationToken = default)
    {
        string? searchUrl = BuildSearchUrl(query);
        if (searchUrl is null)
        {
            return [];
        }

        try
        {
            var searchDoc = await ScraperHttp.LoadAsync(httpClientFactory, searchUrl, AcceptLanguage, cancellationToken);
            if (searchDoc is null)
            {
                logger.LogDebug("Amazon search returned no document for '{Url}'", searchUrl);
                return [];
            }

            var previews = ExtractSearchPreviews(searchDoc);
            if (previews.Count == 0)
            {
                return [];
            }

            int wanted = Math.Clamp(query.Limit, 1, MaxDetailFetches);
            var results = new List<ExternalBookMetadataDto>(wanted);
            for (int i = 0; i < previews.Count && results.Count < wanted; i++)
            {
                if (i > 0)
                {
                    // Small jitter between detail fetches, like Booklore, to look less robotic.
                    await Task.Delay(Random.Shared.Next(400, 900), cancellationToken);
                }

                var dto = await FetchDetailAsync(previews[i], cancellationToken);
                if (dto is not null)
                {
                    results.Add(dto);
                }
            }

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Amazon search failed for '{Url}'", searchUrl);
            return [];
        }
    }

    private static string? BuildSearchUrl(BookMetadataQuery query)
    {
        if (query.HasIsbn)
        {
            string isbn = MetadataNormalization.NormalizeIsbn(query.Isbn!);
            if (isbn.Length > 0)
            {
                return $"https://www.amazon.{Domain}/s?k={Uri.EscapeDataString(isbn)}&i=stripbooks";
            }
        }

        var terms = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(query.Title))
        {
            terms.Add(CleanSearchTerm(query.Title));
        }

        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            terms.Add(CleanSearchTerm(query.Author));
        }

        string term = string.Join(' ', terms.Where(t => t.Length > 0));
        return term.Length == 0
            ? null
            : $"https://www.amazon.{Domain}/s?k={Uri.EscapeDataString(term)}&i=stripbooks";
    }

    private static string CleanSearchTerm(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => NonAlphanumericRegex().Replace(w, string.Empty))
            .Where(w => w.Length > 0);
        return string.Join(' ', words);
    }

    /// <summary>Pulls the ASIN + search-result thumbnail for each non-boxset result on the search page.</summary>
    private List<SearchPreview> ExtractSearchPreviews(HtmlDocument doc)
    {
        var previews = new List<SearchPreview>();
        var resultsContainer = doc.DocumentNode.SelectSingleNode("//span[@data-component-type='s-search-results']");
        var items = resultsContainer?.SelectNodes(".//div[@role='listitem' and @data-asin]")
            ?? doc.DocumentNode.SelectNodes("//div[@role='listitem' and @data-asin]");
        if (items is null)
        {
            return previews;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            string asin = item.GetAttributeValue("data-asin", string.Empty).Trim();
            if (asin.Length == 0 || !seen.Add(asin))
            {
                continue;
            }

            string text = HtmlEntity.DeEntitize(item.InnerText) ?? string.Empty;
            string lower = text.ToLowerInvariant();
            if (lower.Contains("collects books from") || lower.Contains("box set")
                || lower.Contains("books set") || lower.Contains("collection set"))
            {
                continue;
            }

            string? thumbnail = item
                .SelectSingleNode(".//img[contains(concat(' ', normalize-space(@class), ' '), ' s-image ')]")
                ?.GetAttributeValue("src", string.Empty);

            previews.Add(new SearchPreview(asin, MetadataNormalization.NullIfBlank(thumbnail)));
        }

        return previews;
    }

    private async Task<ExternalBookMetadataDto?> FetchDetailAsync(SearchPreview preview, CancellationToken cancellationToken)
    {
        string url = $"https://www.amazon.{Domain}/dp/{preview.Asin}";
        var doc = await ScraperHttp.LoadAsync(httpClientFactory, url, AcceptLanguage, cancellationToken);
        if (doc is null)
        {
            return null;
        }

        var root = doc.DocumentNode;
        var (title, subtitle) = ParseTitle(root);
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var (seriesName, numberInSeries) = ParseSeries(root);
        string? isbn = ParseIsbn(root, "isbn13") ?? ParseIsbn(root, "isbn10");
        var genres = ParseCategories(root);

        return new ExternalBookMetadataDto(
            Provider: Name,
            ProviderId: preview.Asin,
            Title: title!,
            Subtitle: subtitle,
            Description: MetadataNormalization.StripHtml(ParseDescription(root)),
            Language: NormalizeLanguage(SelectText(root, "//*[@id='rpi-attribute-language']//*[contains(concat(' ', normalize-space(@class), ' '), ' rpi-attribute-value ')]//span")),
            Publisher: ParsePublisher(root),
            Isbn: MetadataNormalization.NullIfBlank(isbn),
            PublishedOn: MetadataNormalization.ParseDate(SelectText(root, "//*[@id='rpi-attribute-book_details-publication_date']//*[contains(concat(' ', normalize-space(@class), ' '), ' rpi-attribute-value ')]//span")),
            PageCount: ParsePageCount(root),
            SeriesName: seriesName,
            NumberInSeries: numberInSeries,
            Authors: ParseAuthors(root),
            Genres: genres,
            Tags: [],
            CoverUrl: ParseCover(root) ?? preview.ThumbnailUrl,
            InfoUrl: url);
    }

    private static (string? Title, string? Subtitle) ParseTitle(HtmlNode root)
    {
        string? full = SelectText(root, "//*[@id='productTitle']")
            ?? SelectText(root, "//*[@id='ebooksProductTitle']");
        if (string.IsNullOrWhiteSpace(full))
        {
            return (null, null);
        }

        string[] parts = full.Split(':', 2);
        string title = parts[0].Trim();
        string? subtitle = parts.Length > 1 ? MetadataNormalization.NullIfBlank(parts[1]) : null;
        return (title, subtitle);
    }

    private static IReadOnlyList<string> ParseAuthors(HtmlNode root)
    {
        var nodes = root.SelectNodes("//*[@id='bylineInfo_feature_div']//*[contains(concat(' ', normalize-space(@class), ' '), ' author ')]//a")
            ?? root.SelectNodes("//*[contains(concat(' ', normalize-space(@class), ' '), ' author ')]//a");
        if (nodes is null)
        {
            return [];
        }

        return nodes
            .Select(n => HtmlEntity.DeEntitize(n.InnerText)?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t) && !string.Equals(t, "Follow", StringComparison.OrdinalIgnoreCase))
            .Select(t => t!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ParseDescription(HtmlNode root)
    {
        var expander = root.SelectSingleNode("//*[@data-a-expander-name='book_description_expander']//*[contains(concat(' ', normalize-space(@class), ' '), ' a-expander-content ')]");
        if (expander is not null)
        {
            return expander.InnerHtml;
        }

        var noscript = root.SelectSingleNode("//*[@id='bookDescription_feature_div']//noscript");
        if (noscript is not null)
        {
            return noscript.InnerHtml;
        }

        return root.SelectSingleNode("//div[contains(concat(' ', normalize-space(@class), ' '), ' product-description ')]")?.InnerHtml;
    }

    private static string? ParseIsbn(HtmlNode root, string type)
    {
        string? rpi = SelectText(root, $"//*[@id='rpi-attribute-book_details-{type}']//*[contains(concat(' ', normalize-space(@class), ' '), ' rpi-attribute-value ')]//span");
        if (!string.IsNullOrWhiteSpace(rpi))
        {
            return MetadataNormalization.NormalizeIsbn(rpi);
        }

        // Fall back to the "Product details" bullet list.
        string key = type == "isbn10" ? "ISBN-10" : "ISBN-13";
        string? bullet = ParseDetailBullet(root, key);
        return bullet is null ? null : MetadataNormalization.NormalizeIsbn(bullet);
    }

    private static string? ParsePublisher(HtmlNode root)
    {
        string? rpi = SelectText(root, "//*[@id='rpi-attribute-book_details-publisher']//*[contains(concat(' ', normalize-space(@class), ' '), ' rpi-attribute-value ')]//span");
        if (!string.IsNullOrWhiteSpace(rpi))
        {
            return CleanPublisher(rpi);
        }

        string? bullet = ParseDetailBullet(root, "Publisher");
        return bullet is null ? null : CleanPublisher(bullet);
    }

    private static string CleanPublisher(string raw)
    {
        // Strip a trailing "(edition…)" parenthetical and anything after a ";".
        string value = raw.Split(';')[0].Trim();
        return ParenthesesRegex().Replace(value, string.Empty).Trim();
    }

    /// <summary>Reads a value from the <c>#detailBullets_feature_div</c> &lt;li&gt; whose bold label contains <paramref name="keyPart"/>.</summary>
    private static string? ParseDetailBullet(HtmlNode root, string keyPart)
    {
        var items = root.SelectNodes("//*[@id='detailBullets_feature_div']//li");
        if (items is null)
        {
            return null;
        }

        foreach (var li in items)
        {
            var bold = li.SelectSingleNode(".//span[contains(concat(' ', normalize-space(@class), ' '), ' a-text-bold ')]");
            if (bold is null)
            {
                continue;
            }

            string label = HtmlEntity.DeEntitize(bold.InnerText) ?? string.Empty;
            if (!label.Contains(keyPart, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The value is the next <span> sibling after the bold label.
            var value = bold.SelectSingleNode("following-sibling::span[1]");
            string? text = value is null ? null : HtmlEntity.DeEntitize(value.InnerText)?.Trim();
            return MetadataNormalization.NullIfBlank(text);
        }

        return null;
    }

    private static (string? Name, decimal? Number) ParseSeries(HtmlNode root)
    {
        string? name = SelectText(root, "//*[@id='rpi-attribute-book_details-series']//*[contains(concat(' ', normalize-space(@class), ' '), ' rpi-attribute-value ')]//a//span")
            ?? SelectText(root, "//*[@id='rpi-attribute-book_details-series']//*[contains(concat(' ', normalize-space(@class), ' '), ' rpi-attribute-value ')]//span");

        decimal? number = null;
        string? label = SelectText(root, "//*[@id='rpi-attribute-book_details-series']//*[contains(concat(' ', normalize-space(@class), ' '), ' rpi-attribute-label ')]//span");
        if (!string.IsNullOrWhiteSpace(label))
        {
            var match = SeriesFormatRegex().Match(label);
            if (match.Success && decimal.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out decimal parsed))
            {
                number = parsed;
            }
        }

        return (MetadataNormalization.NullIfBlank(name), number);
    }

    private static int? ParsePageCount(HtmlNode root)
    {
        string? text = SelectText(root, "//*[@id='rpi-attribute-book_details-fiona_pages']//*[contains(concat(' ', normalize-space(@class), ' '), ' rpi-attribute-value ')]//span")
            ?? ParseDetailBullet(root, "print length")
            ?? ParseDetailBullet(root, "Hardcover")
            ?? ParseDetailBullet(root, "Paperback");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string digits = DigitsRegex().Replace(text, string.Empty);
        return int.TryParse(digits, out int pages) && pages > 0 ? pages : null;
    }

    private static IReadOnlyList<string> ParseCategories(HtmlNode root)
    {
        var nodes = root.SelectNodes("//*[@id='detailBullets_feature_div']//*[contains(concat(' ', normalize-space(@class), ' '), ' zg_hrsr ')]//a");
        if (nodes is null)
        {
            return [];
        }

        return nodes
            .Select(n => HtmlEntity.DeEntitize(n.InnerText)?.Replace("(Books)", string.Empty).Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ParseCover(HtmlNode root)
    {
        var img = root.SelectSingleNode("//*[@id='landingImage']")
            ?? root.SelectSingleNode("//*[@id='imgBlkFront']")
            ?? root.SelectSingleNode("//*[@id='ebooksImgBlkFront']");
        if (img is null)
        {
            return null;
        }

        string? hires = MetadataNormalization.NullIfBlank(img.GetAttributeValue("data-old-hires", string.Empty));
        if (hires is not null)
        {
            return hires;
        }

        // data-a-dynamic-image is a JSON map of "url": [w, h]; the first entry is the highest res.
        string dynamic = img.GetAttributeValue("data-a-dynamic-image", string.Empty);
        if (!string.IsNullOrWhiteSpace(dynamic))
        {
            var match = FirstUrlRegex().Match(HtmlEntity.DeEntitize(dynamic));
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return MetadataNormalization.NullIfBlank(img.GetAttributeValue("src", string.Empty));
    }

    private static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        // Amazon shows full language names ("English"); map the common ones to ISO-639-1, otherwise
        // pass the trimmed value through so the editor still shows something useful.
        return language.Trim().ToLowerInvariant() switch
        {
            "english" => "en",
            "french" or "français" => "fr",
            "german" or "deutsch" => "de",
            "spanish" or "español" => "es",
            "italian" or "italiano" => "it",
            "portuguese" or "português" => "pt",
            "dutch" or "nederlands" => "nl",
            "japanese" => "ja",
            "chinese" => "zh",
            "russian" => "ru",
            _ => language.Trim(),
        };
    }

    private static string? SelectText(HtmlNode root, string xpath)
    {
        var node = root.SelectSingleNode(xpath);
        if (node is null)
        {
            return null;
        }

        string text = HtmlEntity.DeEntitize(node.InnerText) ?? string.Empty;
        return MetadataNormalization.NullIfBlank(text);
    }

    private readonly record struct SearchPreview(string Asin, string? ThumbnailUrl);

    [GeneratedRegex(@"[^\p{L}\p{N}\s]", RegexOptions.Compiled)]
    private static partial Regex NonAlphanumericRegex();

    [GeneratedRegex(@"\s*\(.*?\)", RegexOptions.Compiled)]
    private static partial Regex ParenthesesRegex();

    [GeneratedRegex(@"Book\s+(\d+(?:\.\d+)?)\s+of\s+\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SeriesFormatRegex();

    [GeneratedRegex(@"[^\d]", RegexOptions.Compiled)]
    private static partial Regex DigitsRegex();

    [GeneratedRegex("\"(https?://[^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex FirstUrlRegex();
}
