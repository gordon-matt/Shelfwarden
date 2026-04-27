using Docnet.Core;
using Docnet.Core.Converters;
using Docnet.Core.Models;
using Docnet.Core.Readers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig;

namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Pulls metadata + page count from a PDF using <c>UglyToad.PdfPig</c> and renders the first
/// page into a cover thumbnail via <c>Docnet.Core</c> (PDFium under the hood). Docnet ships
/// platform-specific native binaries — Windows, Linux and macOS x64 are covered out of the
/// box; ARM Linux is not, so cover extraction silently no-ops on unsupported platforms rather
/// than blowing up the whole scan.
/// </summary>
public sealed class PdfMetadataExtractor(ILogger<PdfMetadataExtractor> logger) : IEbookMetadataExtractor
{
    /// <summary>Page render dimensions for cover capture. Tall enough to look sharp on the detail page; a single page render is cheap.</summary>
    private static readonly PageDimensions CoverPageDimensions = new(1080, 1920);

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

            cancellationToken.ThrowIfCancellationRequested();
            var cover = RenderCover(filePath);

            return Task.FromResult(new EbookMetadata
            {
                Title = title.Trim(),
                Description = subject,
                PageCount = document.NumberOfPages,
                AuthorNames = authors,
                Tags = tags,
                Cover = cover,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to parse PDF {FilePath}; falling back to filename-only metadata", filePath);
            return Task.FromResult(new EbookMetadata { Title = Path.GetFileNameWithoutExtension(filePath) });
        }
    }

    /// <summary>
    /// Render page 0 of the PDF as a JPEG thumbnail. Returns null on any rendering failure
    /// (e.g. encrypted PDFs, native library not available for the current arch) so the rest
    /// of the scan continues uninterrupted.
    /// </summary>
    private EbookCoverImage? RenderCover(string filePath)
    {
        try
        {
            using var docReader = DocLib.Instance.GetDocReader(filePath, CoverPageDimensions);
            if (docReader.GetPageCount() == 0) return null;

            using var pageReader = docReader.GetPageReader(0);
            int width = pageReader.GetPageWidth();
            int height = pageReader.GetPageHeight();
            if (width <= 0 || height <= 0) return null;

            // PDFium hands back BGRA pixels; ImageSharp can wrap them directly. The naive
            // transparency remover paints any transparent pixel white, which matches what
            // a printed page looks like.
            byte[] rawBytes = pageReader.GetImage(new NaiveTransparencyRemover());

            using var image = Image.LoadPixelData<Bgra32>(rawBytes, width, height);

            // Re-encode as JPEG. The covers directory averages tens of thousands of files at
            // scale, so the smaller / lossy format is the right trade-off.
            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms, new JpegEncoder { Quality = 80 });
            return new EbookCoverImage(ms.ToArray(), "jpg");
        }
        catch (Exception ex)
        {
            // Native library missing, encrypted PDF, malformed page tree — anything goes wrong
            // here and we just skip the cover. The book itself is still usable.
            logger.LogDebug(ex, "Could not render PDF cover for {FilePath}; the book will use the format placeholder", filePath);
            return null;
        }
    }

    private static string? NullIfWhitespace(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
