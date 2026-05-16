namespace Shelfwarden.Models;

/// <summary>Outcome of registering files that already exist on disk outside the extras scan.</summary>
public record RegisterExternalFilesResult(int Added, int Skipped);

/// <summary>Tag on an extra content item (separate from book tags).</summary>
public record AdditionalContentTagDto(int Id, string Name);

/// <summary>Flat projection of a single extra content item, including its author and association lists.</summary>
public record AdditionalContentItemDto(
    int Id,
    string FileName,
    string FilePath,
    string FileExtension,
    long FileSizeBytes,
    DateTime CreatedAt,
    int? AuthorId,
    string? AuthorName,
    bool IsManuallyImported,
    IReadOnlyList<AdditionalContentTagDto> Tags,
    IReadOnlyList<AdditionalContentAssociationDto> Books,
    IReadOnlyList<AdditionalContentAssociationDto> Series);

/// <summary>Sets the same tag list on one or more extra content items (replaces existing tags per item).</summary>
public record SetAdditionalContentTagsRequest
{
    public required IReadOnlyList<int> ItemIds { get; init; }
    public required IReadOnlyList<string> TagNames { get; init; }
}

/// <summary>Minimal id+name pair used to represent associated books or series.</summary>
public record AdditionalContentAssociationDto(int Id, string Name);

/// <summary>Request to bulk-assign content items to an author and optionally move files.</summary>
public record AssignContentToAuthorRequest
{
    public required IReadOnlyList<int> ItemIds { get; init; }
    public required int AuthorId { get; init; }
}

/// <summary>Request to associate a single content item with a set of books or series.</summary>
public record AssociateContentRequest
{
    public required int ItemId { get; init; }
    public required IReadOnlyList<int> Ids { get; init; }
}

/// <summary>Request to rename a content item.</summary>
public record RenameContentItemRequest
{
    public required int ItemId { get; init; }
    public required string NewFileName { get; init; }
}
