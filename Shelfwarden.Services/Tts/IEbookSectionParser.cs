namespace Shelfwarden.Services.Tts;

/// <summary>
/// Parses an ebook into reviewable <see cref="BookSection"/>s and streams the plain text of a
/// chosen set of sections for TTS synthesis. Replaces the older whole-book-only text extractor:
/// the audiobook pipeline now always works in terms of sections so it can skip front matter and
/// split output per chapter.
/// </summary>
public interface IEbookSectionParser
{
    /// <summary>The ebook format this parser handles.</summary>
    EbookFormat Format { get; }

    /// <summary>
    /// Break <paramref name="filePath"/> into sections (chapters + auto-classified front/back
    /// matter) along with a quality signal the UI uses to warn about unreliable detection.
    /// </summary>
    Task<SectionDetectionResult> ParseSectionsAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lazily yield plain-text blocks (roughly paragraphs) from <paramref name="filePath"/>.
    /// When <paramref name="sections"/> is null or empty the whole book is read in reading
    /// order; otherwise only the supplied sections are read, in the order given.
    /// </summary>
    IAsyncEnumerable<string> ExtractAsync(
        string filePath,
        IReadOnlyList<BookSection>? sections,
        CancellationToken cancellationToken = default);
}

/// <summary>Maps an ebook file path to the right section parser.</summary>
public interface IEbookSectionParserFactory
{
    IEbookSectionParser? GetFor(string filePath);
}
