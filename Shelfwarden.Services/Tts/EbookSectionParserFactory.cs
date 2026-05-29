namespace Shelfwarden.Services.Tts;

/// <summary>
/// Default <see cref="IEbookSectionParserFactory"/>. Resolves a parser for a file path by mapping
/// the extension to an <see cref="EbookFormat"/> and looking up the registered parser. Mirrors
/// the scanning metadata-extractor factory.
/// </summary>
public sealed class EbookSectionParserFactory : IEbookSectionParserFactory
{
    private readonly Dictionary<EbookFormat, IEbookSectionParser> byFormat;

    public EbookSectionParserFactory(IEnumerable<IEbookSectionParser> parsers)
        => byFormat = parsers.ToDictionary(p => p.Format);

    public IEbookSectionParser? GetFor(string filePath)
    {
        var format = EbookFormatExtensions.FromExtension(filePath);
        return format == EbookFormat.Unknown ? null : byFormat.GetValueOrDefault(format);
    }
}
