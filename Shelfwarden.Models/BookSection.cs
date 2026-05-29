namespace Shelfwarden.Models;

/// <summary>
/// A logical part of an ebook (a chapter, the copyright page, an appendix, …) produced by the
/// section parsers and reviewed by the user before audiobook generation. The locating fields
/// are format-specific: EPUBs use <see cref="ReadingOrderIndices"/> (positions in the spine /
/// reading order), PDFs use the <see cref="StartPage"/>/<see cref="EndPage"/> page range.
/// </summary>
/// <remarks>
/// This is a mutable class (not a record) on purpose: <see cref="IsIncluded"/> and
/// <see cref="IsChapterBoundary"/> are toggled directly by the section editor UI via two-way
/// binding, and the same instances are serialised into the generation plan that the background
/// job replays.
/// </remarks>
public sealed class BookSection
{
    public string Title { get; set; } = "Untitled";

    public SectionKind Kind { get; set; } = SectionKind.Chapter;

    /// <summary>EPUB only: zero-based positions in the reading order this section spans (inclusive).</summary>
    public List<int> ReadingOrderIndices { get; set; } = [];

    /// <summary>PDF only: 1-based first page of the section (inclusive).</summary>
    public int? StartPage { get; set; }

    /// <summary>PDF only: 1-based last page of the section (inclusive).</summary>
    public int? EndPage { get; set; }

    /// <summary>Whether the section's text is read aloud. Front/back matter defaults to false.</summary>
    public bool IsIncluded { get; set; } = true;

    /// <summary>When chapter splitting is on, a section with this flag starts a new audio file.</summary>
    public bool IsChapterBoundary { get; set; } = true;
}

/// <summary>How a <see cref="BookSection"/> was classified for default include/exclude behaviour.</summary>
public enum SectionKind
{
    /// <summary>Auto-detected copyright, table of contents, dedication, preface, … (excluded by default).</summary>
    FrontMatter = 0,

    /// <summary>Body content (included by default).</summary>
    Chapter = 1,

    /// <summary>Auto-detected index, bibliography, about-the-author, … (excluded by default).</summary>
    BackMatter = 2,

    /// <summary>Could not be classified.</summary>
    Unknown = 3,
}

/// <summary>How confident the parser is in the section breakdown it produced.</summary>
public enum SectionDetectionQuality
{
    /// <summary>From EPUB navigation or PDF bookmarks — reliable.</summary>
    Structured = 0,

    /// <summary>Inferred from PDF heading heuristics — usually right, worth a quick review.</summary>
    Heuristic = 1,

    /// <summary>No structure found; a single catch-all section was returned.</summary>
    None = 2,
}

/// <summary>Result of parsing an ebook into reviewable sections.</summary>
public sealed record SectionDetectionResult(
    IReadOnlyList<BookSection> Sections,
    SectionDetectionQuality Quality,
    string? Warning);
