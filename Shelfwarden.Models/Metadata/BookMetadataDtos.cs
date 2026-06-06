namespace Shelfwarden.Models.Metadata;

/// <summary>
/// Parameters for an online metadata search. At least one of <see cref="Title"/> or
/// <see cref="Isbn"/> should be supplied; <see cref="Author"/> sharpens title searches.
/// </summary>
public record BookMetadataSearchRequest
{
    public string? Title { get; init; }

    public string? Author { get; init; }

    public string? Isbn { get; init; }

    /// <summary>Maximum number of candidates to return per provider.</summary>
    public int Limit { get; init; } = 10;
}

/// <summary>
/// A single provider-agnostic metadata candidate returned by an online source
/// (Google Books, Open Library, …). All fields are optional except <see cref="Title"/>;
/// the consuming UI lets the user pick a candidate and apply it to the edit form.
/// </summary>
public record ExternalBookMetadataDto(
    string Provider,
    string? ProviderId,
    string Title,
    string? Subtitle,
    string? Description,
    string? Language,
    string? Publisher,
    string? Isbn,
    DateTime? PublishedOn,
    int? PageCount,
    string? SeriesName,
    decimal? NumberInSeries,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Tags,
    string? InfoUrl);
