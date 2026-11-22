namespace Shelfwarden.Models;

/// <summary>
/// A user's bookmark. <paramref name="PageNumber"/> is a PDF page and <paramref name="Location"/> is an EPUB position. Either may be <see langword="null"/>.
/// </summary>
/// <param name="Id">Bookmark id.</param>
/// <param name="BookId">Book the bookmark belongs to.</param>
/// <param name="Title">Label, or <see langword="null"/> when the reader left it blank.</param>
/// <param name="PageNumber">PDF page, or <see langword="null"/> for an EPUB.</param>
/// <param name="Location">EPUB position, or <see langword="null"/> for a PDF.</param>
/// <param name="Note">Note, or <see langword="null"/>.</param>
/// <param name="CreatedAt">When the bookmark was saved, in UTC.</param>
public record BookmarkDto(
    int Id,
    int BookId,
    string? Title,
    int? PageNumber,
    string? Location,
    string? Note,
    DateTime CreatedAt);

/// <summary>
/// A bookmark the reader wants to save. Either <see cref="PageNumber"/> or <see cref="Location"/> is required.
/// A blank title or note is stored as <see langword="null"/>.
/// </summary>
public record CreateBookmarkRequest
{
    /// <summary>Label. Blank is stored as <see langword="null"/>.</summary>
    [StringLength(256)]
    public string? Title { get; init; }

    /// <summary>PDF page. Required when <see cref="Location"/> is empty.</summary>
    public int? PageNumber { get; init; }

    /// <summary>EPUB position. Required when <see cref="PageNumber"/> is empty. Blank is stored as <see langword="null"/>.</summary>
    [StringLength(1024)]
    public string? Location { get; init; }

    /// <summary>Note. Blank is stored as <see langword="null"/>.</summary>
    [StringLength(2048)]
    public string? Note { get; init; }
}

/// <summary>New title and note for an existing bookmark. The page and the location are not part of this request.</summary>
public record UpdateBookmarkRequest
{
    /// <summary>New label. Blank clears the stored title. The page and location are left alone.</summary>
    [StringLength(256)]
    public string? Title { get; init; }

    /// <summary>New note. Blank clears the stored note.</summary>
    [StringLength(2048)]
    public string? Note { get; init; }
}