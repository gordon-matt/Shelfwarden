namespace Shelfwarden.Models;

/// <summary>
/// An ordered queue of books a user wants to read. Always per-user (no "global" mode like
/// Collections); the queue order is significant and determines display sequence.
/// </summary>
public record ReadingListDto(
    int Id,
    string Name,
    string? Description,
    string OwnerUserId,
    int BookCount,
    DateTime CreatedAt);

/// <summary>Reading list with items, ordered by Position ascending.</summary>
public record ReadingListDetailDto(
    int Id,
    string Name,
    string? Description,
    string OwnerUserId,
    DateTime CreatedAt,
    IReadOnlyList<ReadingListEntryDto> Items);

/// <summary>Single entry inside a reading list. Holds the position so reorder UIs have something to bind to.</summary>
public record ReadingListEntryDto(
    int Id,
    int Position,
    BookListItemDto Book);

public record CreateReadingListRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    public string? Description { get; init; }
}

public record UpdateReadingListRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    public string? Description { get; init; }
}