using System.Net.Http;
using HtmlAgilityPack;

namespace Shelfwarden.Services.Metadata;

/// <summary>
/// Shared helper for the HTML-scraping metadata providers (Amazon, Goodreads). Issues a request with
/// browser-like headers — mirroring the header set Booklore's Jsoup-based parsers send to look like a
/// real Chrome session — and parses the response into an <see cref="HtmlDocument"/>. Returns null on
/// any non-success status (e.g. Amazon's 503 anti-scraping responses) so callers degrade gracefully.
/// </summary>
internal static class ScraperHttp
{
    private const string ChromeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36";

    public static async Task<HtmlDocument?> LoadAsync(
        IHttpClientFactory httpClientFactory,
        string url,
        string acceptLanguage = "en-US,en;q=0.9",
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("accept-language", acceptLanguage);
        request.Headers.TryAddWithoutValidation("user-agent", ChromeUserAgent);
        request.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Google Chrome\";v=\"137\", \"Chromium\";v=\"137\", \"Not_A Brand\";v=\"24\"");
        request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
        request.Headers.TryAddWithoutValidation("sec-fetch-dest", "document");
        request.Headers.TryAddWithoutValidation("sec-fetch-mode", "navigate");
        request.Headers.TryAddWithoutValidation("sec-fetch-site", "none");
        request.Headers.TryAddWithoutValidation("upgrade-insecure-requests", "1");

        var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string html = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }
}
