namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Routes a file path to the matching <see cref="IEbookMetadataExtractor"/> based on its extension.
/// New formats are added by registering an extractor in DI; this class picks them up automatically.
/// </summary>
public sealed class EbookMetadataExtractorFactory : IEbookMetadataExtractorFactory
{
    private static readonly IReadOnlyDictionary<string, EbookFormat> ExtensionToFormat =
        new Dictionary<string, EbookFormat>(StringComparer.OrdinalIgnoreCase)
        {
            [".epub"] = EbookFormat.Epub,
            [".pdf"] = EbookFormat.Pdf,
        };

    private readonly Dictionary<EbookFormat, IEbookMetadataExtractor> extractorsByFormat;

    public EbookMetadataExtractorFactory(IEnumerable<IEbookMetadataExtractor> extractors)
    {
        extractorsByFormat = extractors.ToDictionary(e => e.Format);
    }

    public IReadOnlyList<string> SupportedExtensions { get; } = [.. ExtensionToFormat.Keys];

    public IEbookMetadataExtractor? GetFor(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext)) return null;

        return ExtensionToFormat.TryGetValue(ext, out var format)
            && extractorsByFormat.TryGetValue(format, out var extractor)
            ? extractor
            : null;
    }
}
