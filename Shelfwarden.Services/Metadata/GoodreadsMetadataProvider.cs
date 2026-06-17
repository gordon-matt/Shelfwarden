using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Shelfwarden.Models.Metadata;

namespace Shelfwarden.Services.Metadata;

/// <summary>
/// Metadata source that scrapes Goodreads, replicating Booklore's <c>GoodReadsParser</c>: search by
/// keyword (or resolve an ISBN directly), then parse each book page's embedded Next.js
/// <c>__NEXT_DATA__</c> Apollo state JSON — which carries the full record (title, description, series,
/// genres, ISBNs, cover) far more reliably than the rendered HTML. Best-effort: any blocking or parse
/// failure yields an empty list rather than throwing.
/// </summary>
public sealed partial class GoodreadsMetadataProvider(
    ILogger<GoodreadsMetadataProvider> logger,
    IHttpClientFactory httpClientFactory) : IBookMetadataProvider
{
    private const string SearchUrl = "https://www.goodreads.com/search?q=";
    private const string BookUrl = "https://www.goodreads.com/book/show/";
    private const string IsbnUrl = "https://www.goodreads.com/book/isbn/";

    /// <summary>Cap on detail-page fetches per search.</summary>
    private const int MaxDetailFetches = 4;

    public string Name => "Goodreads";

    public int Priority => 4;

    public async Task<IReadOnlyList<ExternalBookMetadataDto>> SearchAsync(BookMetadataQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            // ISBN lookups resolve straight to a book page (which still embeds the Apollo JSON).
            if (query.HasIsbn)
            {
                string isbn = MetadataNormalization.NormalizeIsbn(query.Isbn!);
                if (isbn.Length > 0)
                {
                    var byIsbn = await FetchByIsbnAsync(isbn, cancellationToken);
                    if (byIsbn is not null)
                    {
                        return [byIsbn];
                    }
                }
            }

            string? term = BuildSearchTerm(query);
            if (term is null)
            {
                return [];
            }

            var searchDoc = await ScraperHttp.LoadAsync(httpClientFactory, SearchUrl + Uri.EscapeDataString(term), cancellationToken: cancellationToken);
            if (searchDoc is null)
            {
                return [];
            }

            var ids = ExtractBookIds(searchDoc);
            if (ids.Count == 0)
            {
                return [];
            }

            int wanted = Math.Clamp(query.Limit, 1, MaxDetailFetches);
            var results = new List<ExternalBookMetadataDto>(wanted);
            for (int i = 0; i < ids.Count && results.Count < wanted; i++)
            {
                if (i > 0)
                {
                    await Task.Delay(Random.Shared.Next(400, 900), cancellationToken);
                }

                var detailDoc = await ScraperHttp.LoadAsync(httpClientFactory, BookUrl + ids[i], cancellationToken: cancellationToken);
                var dto = detailDoc is null ? null : ParseBook(detailDoc, ids[i]);
                if (dto is not null)
                {
                    results.Add(dto);
                }
            }

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Goodreads search failed for '{Title}' / ISBN '{Isbn}'", query.Title, query.Isbn);
            return [];
        }
    }

    private static string? BuildSearchTerm(BookMetadataQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.Title))
        {
            return string.IsNullOrWhiteSpace(query.Author) ? null : query.Author.Trim();
        }

        return string.IsNullOrWhiteSpace(query.Author)
            ? query.Title.Trim()
            : $"{query.Title.Trim()} {query.Author.Trim()}";
    }

    private async Task<ExternalBookMetadataDto?> FetchByIsbnAsync(string isbn, CancellationToken cancellationToken)
    {
        var doc = await ScraperHttp.LoadAsync(httpClientFactory, IsbnUrl + isbn, cancellationToken: cancellationToken);
        if (doc is null)
        {
            return null;
        }

        // The ISBN endpoint redirects to /book/show/{id}; the canonical id lives in og:url.
        string? ogUrl = doc.DocumentNode
            .SelectSingleNode("//meta[@property='og:url']")
            ?.GetAttributeValue("content", string.Empty);

        string? id = string.IsNullOrWhiteSpace(ogUrl) ? null : ExtractIdFromHref(ogUrl);
        return id is null ? null : ParseBook(doc, id);
    }

    private static List<string> ExtractBookIds(HtmlDocument doc)
    {
        var rows = doc.DocumentNode.SelectNodes("//table[contains(concat(' ', normalize-space(@class), ' '), ' tableList ')]//tr[@itemtype='http://schema.org/Book']");
        var ids = new List<string>();
        if (rows is null)
        {
            return ids;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            string? href = row
                .SelectSingleNode(".//a[contains(concat(' ', normalize-space(@class), ' '), ' bookTitle ')]")
                ?.GetAttributeValue("href", string.Empty);
            string? id = ExtractIdFromHref(href);
            if (id is not null && seen.Add(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static string? ExtractIdFromHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var match = BookShowIdRegex().Match(href);
        return match.Success ? match.Groups[1].Value : null;
    }

    private ExternalBookMetadataDto? ParseBook(HtmlDocument doc, string goodreadsId)
    {
        var script = doc.DocumentNode.SelectSingleNode("//script[@id='__NEXT_DATA__']");
        if (script is null)
        {
            return null;
        }

        string json = HtmlEntity.DeEntitize(script.InnerText) ?? script.InnerText;
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("props", out var props)
                || !props.TryGetProperty("pageProps", out var pageProps)
                || !pageProps.TryGetProperty("apolloState", out var apolloState)
                || apolloState.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            JsonElement? book = null;
            string? seriesName = null;
            string? contributor = null;

            foreach (var entry in apolloState.EnumerateObject())
            {
                if (book is null && entry.Name.Contains("Book:kca:", StringComparison.Ordinal)
                    && entry.Value.ValueKind == JsonValueKind.Object
                    && !string.IsNullOrWhiteSpace(GetString(entry.Value, "title")))
                {
                    book = entry.Value;
                }
                else if (seriesName is null && entry.Name.Contains("Series:kca", StringComparison.Ordinal))
                {
                    seriesName = GetString(entry.Value, "title");
                }
                else if (contributor is null && entry.Name.Contains("Contributor:kca", StringComparison.Ordinal))
                {
                    contributor = GetString(entry.Value, "name");
                }
            }

            if (book is null)
            {
                return null;
            }

            var bookEl = book.Value;
            var (title, subtitle) = SplitTitle(GetString(bookEl, "title"));
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var authors = new List<string>();
            if (!string.IsNullOrWhiteSpace(contributor))
            {
                authors.Add(contributor!.Trim());
            }

            string? isbn = null;
            string? publisher = null;
            string? language = null;
            int? pageCount = null;
            DateTime? publishedOn = null;

            if (bookEl.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object)
            {
                isbn = MetadataNormalization.NullIfBlank(GetString(details, "isbn13"))
                    ?? MetadataNormalization.NullIfBlank(GetString(details, "isbn"));
                publisher = MetadataNormalization.NullIfBlank(GetString(details, "publisher"));
                pageCount = GetInt(details, "numPages");
                publishedOn = EpochMillisToDate(GetString(details, "publicationTime"));
                if (details.TryGetProperty("language", out var lang) && lang.ValueKind == JsonValueKind.Object)
                {
                    language = MetadataNormalization.NullIfBlank(GetString(lang, "name"));
                }
            }

            decimal? numberInSeries = null;
            if (bookEl.TryGetProperty("bookSeries", out var bookSeries)
                && bookSeries.ValueKind == JsonValueKind.Array
                && bookSeries.GetArrayLength() > 0)
            {
                string? position = GetString(bookSeries[0], "userPosition");
                if (decimal.TryParse(position, System.Globalization.CultureInfo.InvariantCulture, out decimal parsed))
                {
                    numberInSeries = parsed;
                }
            }

            return new ExternalBookMetadataDto(
                Provider: Name,
                ProviderId: goodreadsId,
                Title: title!,
                Subtitle: subtitle,
                Description: MetadataNormalization.StripHtml(GetString(bookEl, "description")),
                Language: NormalizeLanguage(language),
                Publisher: publisher,
                Isbn: MetadataNormalization.NullIfBlank(isbn is null ? null : MetadataNormalization.NormalizeIsbn(isbn)),
                PublishedOn: publishedOn,
                PageCount: pageCount is > 0 ? pageCount : null,
                SeriesName: MetadataNormalization.NullIfBlank(seriesName),
                NumberInSeries: numberInSeries,
                Authors: authors,
                Genres: ExtractGenres(bookEl),
                Tags: [],
                CoverUrl: MetadataNormalization.NullIfBlank(GetString(bookEl, "imageUrl")),
                InfoUrl: $"{BookUrl}{goodreadsId}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to parse Goodreads Apollo JSON for id {Id}", goodreadsId);
            return null;
        }
    }

    private static IReadOnlyList<string> ExtractGenres(JsonElement bookEl)
    {
        if (!bookEl.TryGetProperty("bookGenres", out var genresArray) || genresArray.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var genres = new List<string>();
        foreach (var item in genresArray.EnumerateArray())
        {
            if (item.TryGetProperty("genre", out var genre) && genre.ValueKind == JsonValueKind.Object)
            {
                string? name = MetadataNormalization.NullIfBlank(GetString(genre, "name"));
                if (name is not null)
                {
                    genres.Add(name);
                }
            }
        }

        return genres.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (string? Title, string? Subtitle) SplitTitle(string? full)
    {
        if (string.IsNullOrWhiteSpace(full))
        {
            return (null, null);
        }

        string[] parts = full.Split(':', 2);
        string title = parts[0].Trim();
        string? subtitle = parts.Length > 1 ? MetadataNormalization.NullIfBlank(parts[1]) : null;
        return (title.Length == 0 ? null : title, subtitle);
    }

    private static DateTime? EpochMillisToDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !long.TryParse(raw, out long millis))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        return language.Trim().ToLowerInvariant() switch
        {
            "english" => "en",
            "french" => "fr",
            "german" => "de",
            "spanish" => "es",
            "italian" => "it",
            "portuguese" => "pt",
            "dutch" => "nl",
            "japanese" => "ja",
            "chinese" => "zh",
            "russian" => "ru",
            _ => language.Trim(),
        };
    }

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int n) => n,
            JsonValueKind.String when int.TryParse(value.GetString(), out int n) => n,
            _ => null,
        };
    }

    [GeneratedRegex(@"/book/show/(\d+)", RegexOptions.Compiled)]
    private static partial Regex BookShowIdRegex();
}
