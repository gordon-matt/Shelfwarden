using System.Net.Http;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Shelfwarden.Services.Metadata.Authors;

/// <summary>
/// Author metadata scraped from Goodreads. Goodreads has no author API, so we run a keyword search,
/// harvest the distinct author links from the results, then parse each author page
/// (<c>/author/show/{id}</c>) for the name, photo, life dates, full "about" biography and a few
/// notable titles. Best-effort: any blocking or parse failure yields an empty list.
/// </summary>
public sealed partial class GoodreadsAuthorMetadataProvider(
    ILogger<GoodreadsAuthorMetadataProvider> logger,
    IHttpClientFactory httpClientFactory) : IAuthorMetadataProvider
{
    private const string SearchUrl = "https://www.goodreads.com/search?q=";
    private const string AuthorUrl = "https://www.goodreads.com/author/show/";

    /// <summary>Cap on author-page fetches per search (each is a separate scrape).</summary>
    private const int MaxAuthorFetches = 4;

    public string Name => "Goodreads";

    public int Priority => 4;

    public async Task<IReadOnlyList<ExternalAuthorMatchDto>> SearchAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        try
        {
            string trimmed = query.Trim();
            var searchDoc = await ScraperHttp.LoadAsync(httpClientFactory, SearchUrl + Uri.EscapeDataString(trimmed), cancellationToken: cancellationToken);
            if (searchDoc is null)
            {
                return [];
            }

            var ids = ExtractAuthorIds(searchDoc, trimmed);
            if (ids.Count == 0)
            {
                return [];
            }

            int wanted = Math.Clamp(limit, 1, MaxAuthorFetches);
            var results = new List<ExternalAuthorMatchDto>(wanted);
            for (int i = 0; i < ids.Count && results.Count < wanted; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (i > 0)
                {
                    await Task.Delay(Random.Shared.Next(400, 900), cancellationToken);
                }

                var doc = await ScraperHttp.LoadAsync(httpClientFactory, AuthorUrl + ids[i], cancellationToken: cancellationToken);
                var dto = doc is null ? null : ParseAuthor(doc, ids[i]);
                if (dto is not null)
                {
                    results.Add(dto);
                }
            }

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Goodreads author search failed for '{Query}'", query);
            return [];
        }
    }

    private static List<string> ExtractAuthorIds(HtmlDocument doc, string query)
    {
        var links = doc.DocumentNode.SelectNodes("//a[contains(concat(' ', normalize-space(@class), ' '), ' authorName ')]");
        if (links is null)
        {
            return [];
        }

        // Prefer authors whose displayed name looks like the query, but keep the rest as fallbacks.
        var preferred = new List<string>();
        var others = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var link in links)
        {
            string? id = ExtractIdFromHref(link.GetAttributeValue("href", string.Empty));
            if (id is null || !seen.Add(id))
            {
                continue;
            }

            string name = HtmlEntity.DeEntitize(link.InnerText)?.Trim() ?? string.Empty;
            if (name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || query.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                preferred.Add(id);
            }
            else
            {
                others.Add(id);
            }
        }

        preferred.AddRange(others);
        return preferred;
    }

    private ExternalAuthorMatchDto? ParseAuthor(HtmlDocument doc, string goodreadsId)
    {
        try
        {
            var root = doc.DocumentNode;

            string? name = InnerText(root, "//h1[contains(concat(' ', normalize-space(@class), ' '), ' authorName ')]")
                ?? InnerText(root, "//span[@itemprop='name']");
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string? photoUrl = root.SelectSingleNode("//img[@itemprop='image']")?.GetAttributeValue("src", string.Empty)
                ?? root.SelectSingleNode("//div[contains(concat(' ', normalize-space(@class), ' '), ' authorLeftContainer ')]//img")
                    ?.GetAttributeValue("src", string.Empty);

            string? birth = InnerText(root, "//div[@itemprop='birthDate']");
            string? death = InnerText(root, "//div[@itemprop='deathDate']");

            // The "about" section keeps the full (untruncated) text in a span whose id starts with
            // freeTextContainer; the visible span is truncated with a "…more" toggle.
            var bioNode = root.SelectSingleNode("//div[contains(concat(' ', normalize-space(@class), ' '), ' aboutAuthorInfo ')]//span[starts-with(@id,'freeTextContainer')]")
                ?? root.SelectSingleNode("//div[contains(concat(' ', normalize-space(@class), ' '), ' aboutAuthorInfo ')]//span");
            string? biography = bioNode is null
                ? null
                : MetadataNormalization.NullIfBlank(MetadataNormalization.StripHtml(HtmlEntity.DeEntitize(bioNode.InnerHtml)));

            string? topBooks = ExtractTopBooks(root);

            return new ExternalAuthorMatchDto(
                Provider: Name,
                ProviderId: goodreadsId,
                Name: name!.Trim(),
                BirthDate: MetadataNormalization.NullIfBlank(birth),
                DeathDate: MetadataNormalization.NullIfBlank(death),
                Biography: biography,
                PhotoUrl: MetadataNormalization.NullIfBlank(photoUrl),
                TopBooks: topBooks,
                InfoUrl: $"{AuthorUrl}{goodreadsId}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to parse Goodreads author page {Id}", goodreadsId);
            return null;
        }
    }

    private static string? ExtractTopBooks(HtmlNode root)
    {
        var nodes = root.SelectNodes("//a[contains(concat(' ', normalize-space(@class), ' '), ' bookTitle ')]");
        if (nodes is null)
        {
            return null;
        }

        var titles = nodes
            .Select(n => HtmlEntity.DeEntitize(n.InnerText)?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return titles.Count > 0 ? string.Join(", ", titles) : null;
    }

    private static string? InnerText(HtmlNode root, string xpath)
    {
        var node = root.SelectSingleNode(xpath);
        if (node is null)
        {
            return null;
        }

        string text = HtmlEntity.DeEntitize(node.InnerText)?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? ExtractIdFromHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var match = AuthorShowIdRegex().Match(href);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"/author/show/(\d+)", RegexOptions.Compiled)]
    private static partial Regex AuthorShowIdRegex();
}
