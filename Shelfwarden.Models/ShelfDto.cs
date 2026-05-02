namespace Shelfwarden.Models;

public record ShelfDto(
    int Id,
    string Name,
    string? Description,
    DateTime? LastScannedAt,
    DateTime CreatedAt,
    int BookCount,
    IReadOnlyList<ShelfFolderDto> Folders);

public record ShelfFolderDto(int Id, string Path);

public record CreateShelfRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    [StringLength(2048)]
    public string? Description { get; init; }

    [MinLength(1)]
    public required IReadOnlyList<string> Folders { get; init; }
}

public record UpdateShelfRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    [StringLength(2048)]
    public string? Description { get; init; }

    public IReadOnlyList<string> Folders { get; init; } = [];
}
