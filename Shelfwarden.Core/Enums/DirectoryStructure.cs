namespace Shelfwarden.Enums;

/// <summary>
/// How a shelf's folders are laid out on disk. Chosen when a shelf is created and immutable
/// afterwards (it changes how the scanner discovers and reads metadata).
/// </summary>
public enum DirectoryStructure
{
    /// <summary>No required convention — books can live at any depth and metadata comes from the file itself.</summary>
    Unstructured = 0,

    /// <summary>
    /// Calibre library layout (<c>Author/Title (id)/book.ext</c>). The scanner reads the sibling
    /// <c>metadata.opf</c> sidecar and <c>cover.jpg</c> as the authoritative metadata source.
    /// </summary>
    Calibre = 1,
}
