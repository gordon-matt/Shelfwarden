namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Extracts metadata for a single ebook format. Implementations are stateless and safe to register
/// as singletons. <see cref="IEbookMetadataExtractorFactory"/> picks the right implementation per
/// file extension.
/// </summary>
public interface IEbookMetadataExtractor
{
    /// <summary>The ebook format this extractor handles (one extractor per format).</summary>
    EbookFormat Format { get; }

    /// <summary>
    /// Open the file and pull out everything we can. Should not throw for malformed files —
    /// always return <em>something</em> usable (e.g. fall back to the file name as Title) so a
    /// scan never aborts because of one bad book.
    /// </summary>
    Task<EbookMetadata> ExtractAsync(string filePath, CancellationToken cancellationToken = default);
}

public interface IEbookMetadataExtractorFactory
{
    /// <summary>Returns the matching extractor for the given file path, or null if unsupported.</summary>
    IEbookMetadataExtractor? GetFor(string filePath);

    /// <summary>The set of file extensions (lower-case, with leading dot) currently supported.</summary>
    IReadOnlyList<string> SupportedExtensions { get; }
}