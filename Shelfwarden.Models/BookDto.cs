using Shelfwarden.Enums;

namespace Shelfwarden.Models;

/// <summary>
/// Derives a stable cache-busting token for <c>/covers/{id}?v=…</c>. Book IDs are reused after a
/// wipe/rescan; pairing the URL with revision ticks prevents stale browser/CDN images.
/// </summary>
public static class BookCoverCaching
{
    public static long GetCoverCacheVersion(DateTime? lastScannedAt, DateTime? updatedAt, DateTime createdAt)
        => (lastScannedAt ?? updatedAt ?? createdAt).Ticks;
}

/// <summary>Lightweight projection used for browse / search grids.</summary>
public record BookListItemDto(
    int Id,
    string Title,
    string? Subtitle,
    string PrimaryAuthor,
    string? SeriesName,
    decimal? NumberInSeries,
    string? CoverImagePath,
    EbookFormat FileFormat,
    double ProgressPercentage,
    long CoverCacheVersion,
    string? SortTitle = null,
    string? Description = null);

/// <summary>Full book details, including all relationships, used on the detail page.</summary>
public record BookDto(
    int Id,
    string Title,
    string? SortTitle,
    string? Subtitle,
    string? Description,
    string? Language,
    string? Publisher,
    string? Isbn,
    DateTime? PublishedOn,
    int? PageCount,
    string FilePath,
    long FileSizeBytes,
    EbookFormat FileFormat,
    string? CoverImagePath,
    int ShelfId,
    SeriesDto? Series,
    decimal? NumberInSeries,
    IReadOnlyList<AuthorDto> Authors,
    IReadOnlyList<GenreDto> Genres,
    IReadOnlyList<string> Tags,
    DateTime CreatedAt,
    DateTime? LastScannedAt,
    DateTime? UpdatedAt)
{
    /// <summary>Include on cover URLs as <c>?v=</c> so browsers never reuse another book's cached image at the same id.</summary>
    public long CoverCacheVersion => BookCoverCaching.GetCoverCacheVersion(LastScannedAt, UpdatedAt, CreatedAt);
}

/// <summary>User-supplied metadata edits. The scanner-derived fields (FilePath, FileSize, etc) cannot be edited.</summary>
public record UpdateBookRequest
{
    [Required, StringLength(512)]
    public required string Title { get; init; }

    [StringLength(512)]
    public string? SortTitle { get; init; }

    [StringLength(512)]
    public string? Subtitle { get; init; }

    public string? Description { get; init; }

    [StringLength(16)]
    public string? Language { get; init; }

    [StringLength(256)]
    public string? Publisher { get; init; }

    [StringLength(32)]
    public string? Isbn { get; init; }

    public DateTime? PublishedOn { get; init; }

    public int? SeriesId { get; init; }

    public decimal? NumberInSeries { get; init; }

    public IReadOnlyList<int> AuthorIds { get; init; } = [];

    public IReadOnlyList<int> GenreIds { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];
}

public record SaveProgressRequest
{
    [Range(0, 100)]
    public double Percentage { get; init; }

    public int? PageNumber { get; init; }

    public string? Location { get; init; }
}

public record BookProgressDto(
    int BookId,
    double Percentage,
    int? PageNumber,
    string? Location,
    DateTime LastReadAt);