namespace Shelfwarden.Models;

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
    string? SortTitle = null,
    string? Description = null);

/// <summary>Tag attached to a book (id + display name).</summary>
public record TagDto(int Id, string Name);

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
    IReadOnlyList<TagDto> Tags,
    DateTime CreatedAt,
    DateTime? LastScannedAt,
    DateTime? UpdatedAt);

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