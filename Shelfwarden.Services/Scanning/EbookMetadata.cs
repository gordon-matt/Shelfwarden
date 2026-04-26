namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Provider-agnostic snapshot of metadata extracted from an ebook file. Returned by
/// <see cref="IEbookMetadataExtractor.ExtractAsync(string, System.Threading.CancellationToken)"/>
/// and consumed by <see cref="ScannerService"/> when persisting <see cref="Data.Entities.Book"/>s.
/// </summary>
public sealed record EbookMetadata
{
    /// <summary>Best-guess title. Falls back to the file name (without extension) when missing.</summary>
    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public string? Description { get; init; }

    public string? Language { get; init; }

    public string? Publisher { get; init; }

    public string? Isbn { get; init; }

    public DateTime? PublishedOn { get; init; }

    public int? PageCount { get; init; }

    public IReadOnlyList<string> AuthorNames { get; init; } = [];

    public string? SeriesName { get; init; }

    public decimal? NumberInSeries { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Cover image bytes + extension, or null when the file has no embedded cover.</summary>
    public EbookCoverImage? Cover { get; init; }
}

/// <summary>Raw bytes + a file extension hint (e.g. <c>"jpg"</c>) for an extracted cover image.</summary>
public sealed record EbookCoverImage(byte[] Data, string Extension);
