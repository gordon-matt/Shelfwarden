using System.ComponentModel.DataAnnotations;

namespace Shelfwarden.Models;

public record BookmarkDto(
    int Id,
    int BookId,
    string? Title,
    int? PageNumber,
    string? Location,
    string? Note,
    DateTime CreatedAt);

/// <summary>Data the reader sends when the user drops a bookmark at the current spot.</summary>
public record CreateBookmarkRequest
{
    [StringLength(256)]
    public string? Title { get; init; }

    public int? PageNumber { get; init; }

    [StringLength(1024)]
    public string? Location { get; init; }

    [StringLength(2048)]
    public string? Note { get; init; }
}

public record UpdateBookmarkRequest
{
    [StringLength(256)]
    public string? Title { get; init; }

    [StringLength(2048)]
    public string? Note { get; init; }
}
