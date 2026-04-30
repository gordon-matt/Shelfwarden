namespace Shelfwarden.Services.Tts;

/// <summary>
/// Extracts the human-readable body text of an ebook for TTS synthesis. Distinct from
/// <see cref="Scanning.IEbookMetadataExtractor"/> which only pulls catalog metadata —
/// this one walks the entire content and produces a stream of plain text suitable for
/// chunking.
/// </summary>
public interface IBookTextExtractor
{
    /// <summary>The ebook format this extractor handles.</summary>
    EbookFormat Format { get; }

    /// <summary>
    /// Lazily yield blocks of plain text from <paramref name="filePath"/>. Each block is
    /// typically a paragraph or chapter; callers chunk further before sending to TTS.
    /// </summary>
    IAsyncEnumerable<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>Maps an ebook file path to the right text extractor.</summary>
public interface IBookTextExtractorFactory
{
    IBookTextExtractor? GetFor(string filePath);
}
