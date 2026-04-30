namespace Shelfwarden.Services.Tts;

/// <summary>
/// Default <see cref="IBookTextExtractorFactory"/>. Resolves an <see cref="IBookTextExtractor"/>
/// for a file path by mapping the extension to the corresponding <see cref="EbookFormat"/>
/// and looking up the registered extractor for that format.
/// </summary>
public sealed class BookTextExtractorFactory : IBookTextExtractorFactory
{
    private readonly Dictionary<EbookFormat, IBookTextExtractor> byFormat;

    public BookTextExtractorFactory(IEnumerable<IBookTextExtractor> extractors)
    {
        // Keep the last registered extractor per format if the host project ever wants to
        // override one — matches how the scanning factory behaves.
        byFormat = extractors.ToDictionary(e => e.Format);
    }

    public IBookTextExtractor? GetFor(string filePath)
    {
        var format = Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".epub" => EbookFormat.Epub,
            ".pdf" => EbookFormat.Pdf,
            _ => EbookFormat.Unknown,
        };

        return format == EbookFormat.Unknown ? null : byFormat.GetValueOrDefault(format);
    }
}
