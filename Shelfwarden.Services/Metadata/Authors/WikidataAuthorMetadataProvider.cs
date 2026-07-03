using System.Net.Http;
using System.Text.Json;

namespace Shelfwarden.Services.Metadata.Authors;

/// <summary>
/// Author metadata from Wikidata. Resolves a name to entity QIDs via <c>wbsearchentities</c>, keeps
/// only humans (P31 = Q5), then reads each entity's life dates (P569/P570), image (P18) and English
/// Wikipedia sitelink. The biography is pulled from the linked Wikipedia article's intro extract
/// (Wikidata itself has only one-line descriptions), and notable books come from a SPARQL query for
/// works whose author (P50) is the entity.
/// </summary>
public sealed class WikidataAuthorMetadataProvider(
    ILogger<WikidataAuthorMetadataProvider> logger,
    IHttpClientFactory httpClientFactory) : IAuthorMetadataProvider
{
    private const string HumanQid = "Q5";

    public string Name => "Wikidata";

    public int Priority => 1;

    public async Task<IReadOnlyList<ExternalAuthorMatchDto>> SearchAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        // Hydrating a candidate costs several requests (entity + Wikipedia extract + SPARQL), so cap
        // how many we fully resolve regardless of the requested list length.
        int wanted = Math.Clamp(limit, 1, 8);

        try
        {
            var client = httpClientFactory.CreateClient();
            var qids = await SearchEntityIdsAsync(client, query.Trim(), wanted + 4, cancellationToken);

            var matches = new List<ExternalAuthorMatchDto>();
            foreach (string qid in qids)
            {
                if (matches.Count >= wanted)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var match = await HydrateAsync(client, qid, cancellationToken);
                if (match is not null)
                {
                    matches.Add(match);
                }
            }

            return matches;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Wikidata author search failed for '{Query}'", query);
            return [];
        }
    }

    private async Task<List<string>> SearchEntityIdsAsync(HttpClient client, string query, int limit, CancellationToken cancellationToken)
    {
        string url = "https://www.wikidata.org/w/api.php?action=wbsearchentities&format=json&language=en&uselang=en&type=item"
            + $"&limit={limit}&search={Uri.EscapeDataString(query)}";

        using var doc = await GetJsonAsync(client, url, cancellationToken);
        if (doc is null || !doc.RootElement.TryGetProperty("search", out var search) || search.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var ids = new List<string>();
        foreach (var item in search.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                string? value = id.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    ids.Add(value);
                }
            }
        }

        return ids;
    }

    private async Task<ExternalAuthorMatchDto?> HydrateAsync(HttpClient client, string qid, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await GetJsonAsync(client, $"https://www.wikidata.org/wiki/Special:EntityData/{qid}.json", cancellationToken);
            if (doc is null
                || !doc.RootElement.TryGetProperty("entities", out var entities)
                || !entities.TryGetProperty(qid, out var entity))
            {
                return null;
            }

            // Only people — filter out disambiguation pages, works, organisations, etc.
            if (!ClaimContainsEntity(entity, "P31", HumanQid))
            {
                return null;
            }

            string? name = GetLabel(entity, "labels");
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string? birth = ParseTimeClaim(entity, "P569");
            string? death = ParseTimeClaim(entity, "P570");
            string? imageFile = GetStringClaim(entity, "P18");
            string? photoUrl = imageFile is null
                ? null
                : $"https://commons.wikimedia.org/wiki/Special:FilePath/{Uri.EscapeDataString(imageFile)}?width=400";

            string? enwikiTitle = GetEnwikiTitle(entity);
            string? biography = enwikiTitle is null
                ? GetLabel(entity, "descriptions")
                : await FetchWikipediaExtractAsync(client, enwikiTitle, cancellationToken) ?? GetLabel(entity, "descriptions");

            string? topBooks = await FetchTopBooksAsync(client, qid, cancellationToken);

            string infoUrl = enwikiTitle is not null
                ? $"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(enwikiTitle.Replace(' ', '_'))}"
                : $"https://www.wikidata.org/wiki/{qid}";

            return new ExternalAuthorMatchDto(
                Provider: Name,
                ProviderId: qid,
                Name: name!.Trim(),
                BirthDate: birth,
                DeathDate: death,
                Biography: biography,
                PhotoUrl: photoUrl,
                TopBooks: topBooks,
                InfoUrl: infoUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to hydrate Wikidata entity {Qid}", qid);
            return null;
        }
    }

    private async Task<string?> FetchWikipediaExtractAsync(HttpClient client, string title, CancellationToken cancellationToken)
    {
        try
        {
            string url = "https://en.wikipedia.org/w/api.php?action=query&format=json&prop=extracts&exintro&explaintext&redirects=1"
                + $"&titles={Uri.EscapeDataString(title)}";

            using var doc = await GetJsonAsync(client, url, cancellationToken);
            if (doc is null
                || !doc.RootElement.TryGetProperty("query", out var q)
                || !q.TryGetProperty("pages", out var pages)
                || pages.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var page in pages.EnumerateObject())
            {
                if (page.Value.TryGetProperty("extract", out var extract)
                    && extract.ValueKind == JsonValueKind.String)
                {
                    string? text = extract.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to fetch Wikipedia extract for '{Title}'", title);
            return null;
        }
    }

    private async Task<string?> FetchTopBooksAsync(HttpClient client, string qid, CancellationToken cancellationToken)
    {
        try
        {
            string sparql = "SELECT ?workLabel WHERE { "
                + $"?work wdt:P50 wd:{qid}. "
                + "SERVICE wikibase:label { bd:serviceParam wikibase:language \"en\". } "
                + "} LIMIT 5";

            string url = $"https://query.wikidata.org/sparql?format=json&query={Uri.EscapeDataString(sparql)}";

            using var doc = await GetJsonAsync(client, url, cancellationToken);
            if (doc is null
                || !doc.RootElement.TryGetProperty("results", out var results)
                || !results.TryGetProperty("bindings", out var bindings)
                || bindings.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var titles = new List<string>();
            foreach (var binding in bindings.EnumerateArray())
            {
                if (binding.TryGetProperty("workLabel", out var label)
                    && label.TryGetProperty("value", out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    string? title = value.GetString()?.Trim();
                    // Unlabelled works come back as the bare QID; skip those.
                    if (!string.IsNullOrWhiteSpace(title) && !IsBareQid(title))
                    {
                        titles.Add(title);
                    }
                }
            }

            return titles.Count > 0 ? string.Join(", ", titles.Distinct().Take(3)) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to fetch Wikidata works for {Qid}", qid);
            return null;
        }
    }

    private async Task<JsonDocument?> GetJsonAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // Wikimedia APIs require a descriptive User-Agent or they return 403/429.
        request.Headers.TryAddWithoutValidation("User-Agent", MetadataHttp.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json,application/sparql-results+json");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static bool IsBareQid(string value)
        => value.Length > 1 && value[0] == 'Q' && value[1..].All(char.IsDigit);

    private static bool ClaimContainsEntity(JsonElement entity, string property, string targetQid)
    {
        foreach (var snak in EnumerateMainSnaks(entity, property))
        {
            if (snak.TryGetProperty("datavalue", out var dv)
                && dv.TryGetProperty("value", out var value)
                && value.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && string.Equals(id.GetString(), targetQid, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetStringClaim(JsonElement entity, string property)
    {
        foreach (var snak in EnumerateMainSnaks(entity, property))
        {
            if (snak.TryGetProperty("datavalue", out var dv)
                && dv.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string? ParseTimeClaim(JsonElement entity, string property)
    {
        foreach (var snak in EnumerateMainSnaks(entity, property))
        {
            if (!snak.TryGetProperty("datavalue", out var dv)
                || !dv.TryGetProperty("value", out var value)
                || !value.TryGetProperty("time", out var time)
                || time.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? raw = time.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            // Format: "+1866-06-11T00:00:00Z" (leading sign). precision 11 = day, 9 = year.
            int precision = value.TryGetProperty("precision", out var p) && p.TryGetInt32(out int pr) ? pr : 9;
            string body = raw.TrimStart('+', '-');
            string[] parts = body.Split('T')[0].Split('-');
            if (parts.Length == 0 || parts[0].Length < 4)
            {
                continue;
            }

            string year = parts[0];
            return precision >= 11 && parts.Length >= 3 && parts[1] != "00" && parts[2] != "00"
                ? $"{year}-{parts[1]}-{parts[2]}"
                : year;
        }

        return null;
    }

    private static IEnumerable<JsonElement> EnumerateMainSnaks(JsonElement entity, string property)
    {
        if (!entity.TryGetProperty("claims", out var claims)
            || !claims.TryGetProperty(property, out var statements)
            || statements.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var statement in statements.EnumerateArray())
        {
            if (statement.TryGetProperty("mainsnak", out var mainsnak))
            {
                yield return mainsnak;
            }
        }
    }

    private static string? GetLabel(JsonElement entity, string section)
        => entity.TryGetProperty(section, out var labels)
            && labels.TryGetProperty("en", out var en)
            && en.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetEnwikiTitle(JsonElement entity)
        => entity.TryGetProperty("sitelinks", out var sitelinks)
            && sitelinks.TryGetProperty("enwiki", out var enwiki)
            && enwiki.TryGetProperty("title", out var title)
            && title.ValueKind == JsonValueKind.String
            ? title.GetString()
            : null;
}
