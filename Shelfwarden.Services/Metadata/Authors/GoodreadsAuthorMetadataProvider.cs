using System.Net.Http;
using System.Text.Json;
using HtmlAgilityPack;

namespace Shelfwarden.Services.Metadata.Authors;

/// <summary>
/// Author metadata scraped from Goodreads. Goodreads has no author API and its HTML search page is
/// behind an AWS WAF JavaScript challenge, so we discover author ids through the public
/// <c>book/auto_complete</c> JSON endpoint (each result carries the contributing author's id/name),
/// then parse each author page (<c>/author/show/{id}</c>) for the name, photo, life dates, full
/// "about" biography and a few notable titles. Best-effort: any blocking or parse failure yields an
/// empty list.
/// </summary>
public sealed class GoodreadsAuthorMetadataProvider(
    ILogger<GoodreadsAuthorMetadataProvider> logger,
    IHttpClientFactory httpClientFactory) : IAuthorMetadataProvider
{
    private const string AutoCompleteUrl = "https://www.goodreads.com/book/auto_complete?format=json&q=";
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
            string? json = await ScraperHttp.LoadStringAsync(
                httpClientFactory,
                AutoCompleteUrl + Uri.EscapeDataString(trimmed),
                accept: "application/json, text/plain, */*",
                cancellationToken: cancellationToken);
            if (json is null)
            {
                return [];
            }

            var ids = ExtractAuthorIds(json, trimmed);
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

    /// <summary>
    /// Reads the <c>book/auto_complete</c> JSON array and returns the distinct author ids it references.
    /// Authors whose name looks like the query are ordered first so the best match is fetched even when
    /// the fetch cap trims the list.
    /// </summary>
    private List<string> ExtractAuthorIds(string json, string query)
    {
        var preferred = new List<string>();
        var others = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("author", out var author)
                    || author.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? id = ReadAuthorId(author);
                if (id is null || !seen.Add(id))
                {
                    continue;
                }

                string name = author.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                    ? nameEl.GetString()?.Trim() ?? string.Empty
                    : string.Empty;

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
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Failed to parse Goodreads auto_complete response for '{Query}'", query);
            return [];
        }

        preferred.AddRange(others);
        return preferred;
    }

    private static string? ReadAuthorId(JsonElement author)
    {
        if (!author.TryGetProperty("id", out var idEl))
        {
            return null;
        }

        return idEl.ValueKind switch
        {
            JsonValueKind.Number when idEl.TryGetInt64(out long n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.String => MetadataNormalization.NullIfBlank(idEl.GetString()),
            _ => null,
        };
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
}
