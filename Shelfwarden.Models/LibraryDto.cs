namespace Shelfwarden.Models;

public record LibraryDto(
    int Id,
    string Name,
    string? Description,
    DateTime? LastScannedAt,
    DateTime CreatedAt,
    int BookCount,
    IReadOnlyList<LibraryFolderDto> Folders);

public record LibraryFolderDto(int Id, string Path);

public record CreateLibraryRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    [StringLength(2048)]
    public string? Description { get; init; }

    [MinLength(1)]
    public required IReadOnlyList<string> Folders { get; init; }
}

public record UpdateLibraryRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    [StringLength(2048)]
    public string? Description { get; init; }

    public IReadOnlyList<string> Folders { get; init; } = [];
}