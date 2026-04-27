namespace Shelfwarden.Enums;

/// <summary>
/// How the batch-edit page should treat a multi-value field (Authors / Genres / Tags) when
/// applying changes across many books at once.
/// </summary>
public enum ChipApplyMode
{
    /// <summary>Add the new values, keeping any pre-existing entries on each book.</summary>
    Append = 0,

    /// <summary>Throw away the book's current values and use only what the user entered.</summary>
    Replace = 1,
}
