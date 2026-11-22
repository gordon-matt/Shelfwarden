namespace Shelfwarden.Models;

/// <summary>Lightweight projection used for browse / search grids.</summary>
/// <param name="Id">Book id.</param>
/// <param name="Title">Title shown in the grid.</param>
/// <param name="Subtitle">Subtitle, or <see langword="null"/> when the book has none.</param>
/// <param name="PrimaryAuthor">Name shown as the author.</param>
/// <param name="SeriesName">Series name, or <see langword="null"/> when the book is not in a series.</param>
/// <param name="NumberInSeries">Position in that series, or <see langword="null"/> when it has none.</param>
/// <param name="CoverImagePath">Relative cover path, or <see langword="null"/> when there is no cover.</param>
/// <param name="FileFormat">EPUB or PDF.</param>
/// <param name="ProgressPercentage">The calling user's progress, from 0 through 100.</param>
/// <param name="SortTitle">Title used for sorting. <see langword="null"/> falls back to <paramref name="Title"/>.</param>
/// <param name="Description">Description, or <see langword="null"/> when none is stored.</param>
/// <param name="Rating">The calling user's stars out of 5. <see langword="null"/> or 0 means unrated.</param>
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
    string? Description = null,
    int? Rating = null);

/// <summary>Tag attached to a book. Names are matched without case.</summary>
/// <param name="Id">Tag id.</param>
/// <param name="Name">Display name.</param>
public record TagDto(int Id, string Name);

/// <summary>Full book details, including all relationships, used on the detail page.</summary>
/// <param name="Id">Book id.</param>
/// <param name="Title">Title.</param>
/// <param name="SortTitle">Title used for sorting, or <see langword="null"/> to use <paramref name="Title"/>.</param>
/// <param name="Subtitle">Subtitle, or <see langword="null"/>.</param>
/// <param name="Description">Description, or <see langword="null"/>.</param>
/// <param name="Language">Language, or <see langword="null"/>.</param>
/// <param name="Publisher">Publisher, or <see langword="null"/>.</param>
/// <param name="Isbn">ISBN, or <see langword="null"/>.</param>
/// <param name="PublishedOn">Publication date, or <see langword="null"/>.</param>
/// <param name="PageCount">Page count, or <see langword="null"/> when it is not known.</param>
/// <param name="FilePath">Path of the ebook file. Deleting the book does not delete this file.</param>
/// <param name="FileSizeBytes">Size of that file, in bytes.</param>
/// <param name="FileFormat">EPUB or PDF.</param>
/// <param name="CoverImagePath">Relative cover path, or <see langword="null"/>.</param>
/// <param name="ShelfId">Shelf that holds the book.</param>
/// <param name="Series">Series, or <see langword="null"/> when the book is not in one.</param>
/// <param name="NumberInSeries">Position in <paramref name="Series"/>, or <see langword="null"/>.</param>
/// <param name="Authors">Authors, in stored order. Empty when the book has none.</param>
/// <param name="Genres">Genres. Empty when the book has none.</param>
/// <param name="Tags">Tags. Empty when the book has none.</param>
/// <param name="CreatedAt">When the book was added, in UTC.</param>
/// <param name="LastScannedAt">When a scan last saw the file, or <see langword="null"/>.</param>
/// <param name="UpdatedAt">When a user last edited the metadata, or <see langword="null"/> when nobody has.</param>
/// <param name="Rating">The calling user's stars out of 5. <see langword="null"/> or 0 means unrated.</param>
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
    DateTime? UpdatedAt,
    int? Rating = null);

/// <summary>The calling user's per-book state: their star rating and free-form notes.</summary>
/// <param name="BookId">Book id.</param>
/// <param name="Rating">Stars out of 5. <see langword="null"/> or 0 means unrated.</param>
/// <param name="Notes">Free-form notes, or <see langword="null"/> when none are stored.</param>
public record BookUserDto(int BookId, int? Rating, string? Notes);

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

/// <summary>
/// The calling user's new place in a book. <see cref="Percentage"/> runs from 0 to 100.
/// <see cref="PageNumber"/> is a PDF page. <see cref="Location"/> is an EPUB position.
/// </summary>
public record SaveProgressRequest
{
    /// <summary>Progress, from 0 through 100.</summary>
    [Range(0, 100)]
    public double Percentage { get; init; }

    /// <summary>PDF page. Leave empty for an EPUB.</summary>
    public int? PageNumber { get; init; }

    /// <summary>EPUB position. Leave empty for a PDF.</summary>
    public string? Location { get; init; }
}

/// <summary>
/// The calling user's saved place in a book. <paramref name="PageNumber"/> is a PDF page and
/// <paramref name="Location"/> is an EPUB position.
/// </summary>
/// <param name="BookId">Book id.</param>
/// <param name="Percentage">Progress, from 0 through 100.</param>
/// <param name="PageNumber">PDF page, or <see langword="null"/> for an EPUB.</param>
/// <param name="Location">EPUB position, or <see langword="null"/> for a PDF.</param>
/// <param name="LastReadAt">When this place was saved, in UTC.</param>
public record BookProgressDto(
    int BookId,
    double Percentage,
    int? PageNumber,
    string? Location,
    DateTime LastReadAt);