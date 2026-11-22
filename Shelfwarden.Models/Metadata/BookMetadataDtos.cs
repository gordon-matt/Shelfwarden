namespace Shelfwarden.Models.Metadata;

/// <summary>
/// Parameters for an online metadata search. At least one of <see cref="Title"/> or
/// <see cref="Isbn"/> should be supplied; <see cref="Author"/> sharpens title searches.
/// </summary>
public record BookMetadataSearchRequest
{
    /// <summary>Title to search for.</summary>
    public string? Title { get; init; }

    /// <summary>Author used to narrow a title search.</summary>
    public string? Author { get; init; }

    /// <summary>ISBN. Used in preference to a title search when it is set.</summary>
    public string? Isbn { get; init; }

    /// <summary>Maximum number of candidates to return per provider.</summary>
    public int Limit { get; init; } = 10;
}

/// <summary>
/// A single provider-agnostic metadata candidate returned by an online source
/// (Google Books, Open Library, …). All fields are optional except <see cref="Title"/>;
/// the consuming UI lets the user pick a candidate and apply it to the edit form.
/// </summary>
/// <param name="Provider">Source name, such as Google Books.</param>
/// <param name="ProviderId">That source's id, or <see langword="null"/>.</param>
/// <param name="Title">Title. This is the one field a candidate always has.</param>
/// <param name="Subtitle">Subtitle, or <see langword="null"/>.</param>
/// <param name="Description">Description, or <see langword="null"/>.</param>
/// <param name="Language">Language, or <see langword="null"/>.</param>
/// <param name="Publisher">Publisher, or <see langword="null"/>.</param>
/// <param name="Isbn">ISBN, or <see langword="null"/>.</param>
/// <param name="PublishedOn">Publication date, or <see langword="null"/>.</param>
/// <param name="PageCount">Page count, or <see langword="null"/>.</param>
/// <param name="SeriesName">Series name, or <see langword="null"/>.</param>
/// <param name="NumberInSeries">Position in that series, or <see langword="null"/>.</param>
/// <param name="Authors">Author names. Empty when the source gave none.</param>
/// <param name="Genres">Genre names. Empty when the source gave none.</param>
/// <param name="Tags">Tag names. Empty when the source gave none.</param>
/// <param name="CoverUrl">Cover address, or <see langword="null"/>.</param>
/// <param name="InfoUrl">Page about this book at the source, or <see langword="null"/>.</param>
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
    string? CoverUrl,
    string? InfoUrl);