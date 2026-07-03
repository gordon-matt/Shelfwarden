namespace Shelfwarden.Services.Metadata.Authors;

/// <summary>
/// One online source of author metadata (biography, photo, life dates, notable books). Mirrors the
/// book-side <see cref="IBookMetadataProvider"/>: implementations are stateless singletons that talk
/// to public APIs / pages via <see cref="System.Net.Http.IHttpClientFactory"/> and translate the
/// response into <see cref="ExternalAuthorMatchDto"/> candidates. Adding a new source (Wikidata,
/// Goodreads, …) is just another registration — <see cref="IAuthorService"/> queries every provider.
/// </summary>
public interface IAuthorMetadataProvider
{
    /// <summary>Stable display name shown in the provider picker and stamped on each match.</summary>
    string Name { get; }

    /// <summary>Lower wins when merging/ordering results from multiple providers.</summary>
    int Priority { get; }

    /// <summary>
    /// Returns author candidates for the query, richest match first. Implementations should swallow
    /// transient/HTTP errors and return an empty list rather than throwing, so one failing source
    /// never sinks the whole search. Each returned match must carry the full biography and a photo
    /// URL (when available) so the caller can import without a second lookup.
    /// </summary>
    Task<IReadOnlyList<ExternalAuthorMatchDto>> SearchAsync(string query, int limit, CancellationToken cancellationToken = default);
}
