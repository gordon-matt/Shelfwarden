namespace Shelfwarden.Models;

/// <summary>Outcome of registering files that already exist on disk outside the extras scan.</summary>
/// <param name="Added">Files that were added to the library.</param>
/// <param name="Skipped">Normalized paths that were not added: already registered, missing, a directory, or excluded by the extras filter.</param>
public record RegisterExternalFilesResult(int Added, int Skipped);

/// <summary>Tag on an extra content item (separate from book tags).</summary>
/// <param name="Id">Tag id.</param>
/// <param name="Name">Display name.</param>
public record AdditionalContentTagDto(int Id, string Name);

/// <summary>Flat projection of a single extra content item, including its author and association lists.</summary>
/// <param name="Id">Item id.</param>
/// <param name="FileName">Display name. Renaming this does not rename the file on disk.</param>
/// <param name="FilePath">Path of the file.</param>
/// <param name="FileExtension">Extension, including the leading dot.</param>
/// <param name="FileSizeBytes">Size of the file, in bytes.</param>
/// <param name="CreatedAt">When the library row was created, in UTC.</param>
/// <param name="AuthorId">Author the item is assigned to, or <see langword="null"/>.</param>
/// <param name="AuthorName">That author's name, or <see langword="null"/>.</param>
/// <param name="IsManuallyImported"><see langword="true"/> when the file was registered in place and must not be moved or deleted.</param>
/// <param name="Tags">Tags on this item.</param>
/// <param name="Books">Books this item is linked to.</param>
/// <param name="Series">Series this item is linked to.</param>
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
/// <param name="Id">Book or series id.</param>
/// <param name="Name">Book or series name.</param>
public record AdditionalContentAssociationDto(int Id, string Name);

/// <summary>
/// Assigns extra content items to an author. Scanned files are moved under that author's extras
/// folder. Manually imported files stay where they are.
/// </summary>
public record AssignContentToAuthorRequest
{
    public required IReadOnlyList<int> ItemIds { get; init; }
    public required int AuthorId { get; init; }
}

/// <summary>Book or series ids to add to one extra content item. Links that already exist stay.</summary>
public record AssociateContentRequest
{
    public required int ItemId { get; init; }
    public required IReadOnlyList<int> Ids { get; init; }
}

/// <summary>New display name for an extra content item. The file on disk is not renamed.</summary>
public record RenameContentItemRequest
{
    public required int ItemId { get; init; }

    /// <summary>Stored display name. A blank value is rejected.</summary>
    public required string NewFileName { get; init; }
}