using System.ComponentModel.DataAnnotations;

namespace Shelfwarden.Models;

/// <summary>
/// A user-curated themed grouping of books. <see cref="IsGlobal"/> is true if this is shared
/// across all users (created by admins; OwnerUserId == Constants.GlobalUserId).
/// </summary>
public record CollectionDto(
    int Id,
    string Name,
    string? Description,
    string OwnerUserId,
    bool IsGlobal,
    int BookCount,
    DateTime CreatedAt);

/// <summary>Detail projection that also embeds the books inside the collection.</summary>
public record CollectionDetailDto(
    int Id,
    string Name,
    string? Description,
    string OwnerUserId,
    bool IsGlobal,
    DateTime CreatedAt,
    IReadOnlyList<BookListItemDto> Books);

public record CreateCollectionRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// Admins can mark a collection as global so it shows up for every user. Ignored for
    /// non-administrator callers — they always get a personal collection.
    /// </summary>
    public bool IsGlobal { get; init; }
}

public record UpdateCollectionRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    public string? Description { get; init; }
}
