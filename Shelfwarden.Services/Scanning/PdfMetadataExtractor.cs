using UglyToad.PdfPig;

namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Pulls metadata + page count from a PDF using <c>UglyToad.PdfPig</c>. PdfPig is pure-managed
/// (no native dependencies), which keeps the desktop build portable. Cover image extraction
/// requires rendering and is intentionally out of scope here — the UI falls back to a generic
/// PDF placeholder when <see cref="EbookMetadata.Cover"/> is null.
/// </summary>
public sealed class PdfMetadataExtractor(ILogger<PdfMetadataExtractor> logger) : IEbookMetadataExtractor
{
    public EbookFormat Format => EbookFormat.Pdf;

    public Task<EbookMetadata> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var document = PdfDocument.Open(filePath);
            cancellationToken.ThrowIfCancellationRequested();

            var info = document.Information;

            string title = NullIfWhitespace(info.Title) ?? Path.GetFileNameWithoutExtension(filePath);
            string? author = NullIfWhitespace(info.Author);
            string? subject = NullIfWhitespace(info.Subject);
            string? keywords = NullIfWhitespace(info.Keywords);

            var authors = string.IsNullOrEmpty(author)
                ? Array.Empty<string>()
                : author
                    .Split([',', ';', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            var tags = string.IsNullOrEmpty(keywords)
                ? Array.Empty<string>()
                : keywords
                    .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            return Task.FromResult(new EbookMetadata
            {
                Title = title.Trim(),
                Description = subject,
                PageCount = document.NumberOfPages,
                AuthorNames = authors,
                Tags = tags,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to parse PDF {FilePath}; falling back to filename-only metadata", filePath);
            return Task.FromResult(new EbookMetadata { Title = Path.GetFileNameWithoutExtension(filePath) });
        }
    }

    private static string? NullIfWhitespace(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
