using System.Runtime.CompilerServices;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Streams page-by-page text from a PDF using <c>UglyToad.PdfPig</c>. PdfPig's content stream
/// parser doesn't really expose paragraph boundaries — we fall back to <see cref="ContentOrderTextExtractor"/>
/// which preserves the visual reading order PdfPig inferred at parse time.
/// </summary>
public sealed class PdfTextExtractor(ILogger<PdfTextExtractor> logger) : IBookTextExtractor
{
    public EbookFormat Format => EbookFormat.Pdf;

    public async IAsyncEnumerable<string> ExtractAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        PdfDocument? document = null;
        try
        {
            document = PdfDocument.Open(filePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to open PDF {FilePath} for TTS extraction", filePath);
            yield break;
        }

        if (document is null)
        {
            yield break;
        }

        try
        {
            int pageCount = document.NumberOfPages;
            for (int i = 1; i <= pageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Page page;
                try
                {
                    page = document.GetPage(i);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Skipping unreadable PDF page {PageNumber} of {File}", i, filePath);
                    continue;
                }

                string text;
                try
                {
                    text = ContentOrderTextExtractor.GetText(page) ?? string.Empty;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Falling back to raw text on page {PageNumber} of {File}", i, filePath);
                    text = page.Text ?? string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return text;
                }

                // Yield to the scheduler periodically so very long PDFs don't monopolise a
                // worker thread. Without this an N-thousand-page document would never check
                // its cancellation token between pages.
                await Task.Yield();
            }
        }
        finally
        {
            document.Dispose();
        }
    }
}