using Shelfwarden.Models.Metadata;

namespace Shelfwarden.Services.Metadata;

/// <summary>
/// Normalised inputs for a single provider query. Mirrors Calibre's
/// <c>identify(title, authors, identifiers)</c> contract: an ISBN search is preferred when present,
/// otherwise a title (+ optional author) search is used.
/// </summary>
public sealed record BookMetadataQuery
{
    public string? Title { get; init; }

    public string? Author { get; init; }

    public string? Isbn { get; init; }

    public int Limit { get; init; } = 10;

    public bool HasIsbn => !string.IsNullOrWhiteSpace(Isbn);

    public bool HasTitleOrAuthor =>
        !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Author);

    public bool IsEmpty => !HasIsbn && !HasTitleOrAuthor;
}

/// <summary>
/// One online metadata source. Implementations are stateless singletons that issue HTTP requests
/// via <see cref="System.Net.Http.IHttpClientFactory"/> and translate the response into
/// <see cref="ExternalBookMetadataDto"/> candidates. New sources (Amazon, Goodreads, …) can be added
/// by registering another implementation — the aggregating <see cref="IBookMetadataService"/> queries
/// every registered provider in parallel.
/// </summary>
public interface IBookMetadataProvider
{
    /// <summary>Stable display name shown in the UI and used to deprioritise/rank sources.</summary>
    string Name { get; }

    /// <summary>
    /// Relative ordering hint when two candidates tie on quality (lower wins). Lets us prefer the
    /// source with cleaner data the way Calibre's <c>source_relevance</c> does.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Returns metadata candidates for the query, best match first. Implementations should swallow
    /// transient/HTTP errors and return an empty list rather than throwing, so one failing source
    /// never sinks the whole search.
    /// </summary>
    Task<IReadOnlyList<ExternalBookMetadataDto>> SearchAsync(BookMetadataQuery query, CancellationToken cancellationToken = default);
}
