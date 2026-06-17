using System.Net.Http;
using Shelfwarden.Services.Scanning;
using Shelfwarden.Services.Storage;
using SixLabors.ImageSharp;

namespace Shelfwarden.Services;

public sealed class BookCoverService(
    ILogger<BookCoverService> logger,
    IRepository<Book> bookRepository,
    IStoragePathProvider storage,
    IEbookMetadataExtractorFactory extractorFactory,
    IUserContextService userContext,
    IHttpClientFactory httpClientFactory) : IBookCoverService
{
    /// <summary>Reject anything larger than this — covers are images, not archives.</summary>
    private const long MaxCoverBytes = 20 * 1024 * 1024;

    public async Task<Result> SetCoverFromUrlAsync(int bookId, string imageUrl, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(imageUrl)
            || !Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Result.Invalid(new ValidationError(nameof(imageUrl), "A valid image URL is required."));
        }

        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == bookId,
            CancellationToken = cancellationToken,
        });
        if (book is null)
        {
            return Result.NotFound($"Book {bookId} not found.");
        }

        try
        {
            byte[]? bytes = await DownloadAsync(uri, cancellationToken);
            if (bytes is null || bytes.Length == 0)
            {
                return Result.Error("Could not download the selected cover image.");
            }

            string? extension = DetectImageExtension(bytes);
            if (extension is null)
            {
                return Result.Error("The selected cover is not a recognised image.");
            }

            await SaveCoverAsync(book, new EbookCoverImage(bytes, extension), cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to apply online cover for book {BookId} from {Url}", bookId, imageUrl);
            return Result.Error("Could not apply the selected cover image.");
        }
    }

    public async Task<Result> RevertCoverToEmbeddedAsync(int bookId, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == bookId,
            CancellationToken = cancellationToken,
        });
        if (book is null)
        {
            return Result.NotFound($"Book {bookId} not found.");
        }

        if (string.IsNullOrWhiteSpace(book.FilePath) || !File.Exists(book.FilePath))
        {
            return Result.Error("The book's file could not be found on disk.");
        }

        var extractor = extractorFactory.GetFor(book.FilePath);
        if (extractor is null)
        {
            return Result.Error("This file format does not support cover extraction.");
        }

        try
        {
            var metadata = await extractor.ExtractAsync(book.FilePath, cancellationToken);
            if (metadata.Cover is null)
            {
                return Result.Error("The book's file does not contain an embedded cover.");
            }

            await SaveCoverAsync(book, metadata.Cover, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to revert cover to embedded for book {BookId}", bookId);
            return Result.Error("Could not read the embedded cover from the book's file.");
        }
    }

    private async Task<byte[]?> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Accept", "image/*");
        request.Headers.TryAddWithoutValidation("User-Agent", MetadataHttpUserAgent);

        var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("Cover download from {Url} returned {Status}", uri, (int)response.StatusCode);
            return null;
        }

        if (response.Content.Headers.ContentLength is > MaxCoverBytes)
        {
            logger.LogDebug("Cover at {Url} exceeds the size limit ({Length} bytes)", uri, response.Content.Headers.ContentLength);
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>Validates the bytes are a real image and returns the canonical file extension, else null.</summary>
    private static string? DetectImageExtension(byte[] bytes)
    {
        try
        {
            var format = Image.DetectFormat(bytes);
            string? extension = format.FileExtensions.FirstOrDefault();
            return string.IsNullOrWhiteSpace(extension) ? "jpg" : extension.ToLowerInvariant();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task SaveCoverAsync(Book book, EbookCoverImage cover, CancellationToken cancellationToken)
    {
        string relativePath = await storage.SaveCoverAsync(book.Id, cover, cancellationToken);
        book.CoverImagePath = relativePath;
        await bookRepository.UpdateAsync(book);
    }

    // Same descriptive UA the metadata providers use; duplicated here to avoid widening the
    // internal MetadataHttp helper's visibility.
    private const string MetadataHttpUserAgent = "Shelfwarden/1.0 (+https://github.com/Shelfwarden)";
}
