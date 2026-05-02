using System.Diagnostics;
using Extenso;
using Humanizer;
using Shelfwarden.Services.Storage;

namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Recursive shelf scanner. For every supported file under the shelf's folders this:
///   1. Looks up the existing <see cref="Book"/> by <c>(ShelfId, FilePath)</c>.
///   2. Skips it when <see cref="FileInfo.LastWriteTimeUtc"/> + size match the stored values.
///   3. Otherwise extracts metadata (via <see cref="IEbookMetadataExtractor"/>), persists/updates
///      the book + author/series/genre relations, and writes the cover bytes to disk.
/// At the end it removes books whose files have disappeared and stamps <see cref="Shelf.LastScannedAt"/>.
/// </summary>
public sealed class ScannerService(
    ILogger<ScannerService> logger,
    IEbookMetadataExtractorFactory extractorFactory,
    IStoragePathProvider storage,
    IScanProgressTracker progressTracker,
    IRepository<Shelf> shelfRepository,
    IRepository<Book> bookRepository,
    IRepository<Author> authorRepository,
    IRepository<BookAuthor> bookAuthorRepository,
    IRepository<Series> seriesRepository,
    IRepository<Genre> genreRepository,
    IRepository<BookGenre> bookGenreRepository) : IScannerService
{
    public async Task<Result<ScanResult>> ScanShelfAsync(int shelfId, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var shelf = await shelfRepository.FindOneAsync(new SearchOptions<Shelf>
        {
            Query = s => s.Id == shelfId,
            Include = q => q.Include(s => s.Folders),
        });

        if (shelf is null)
        {
            logger.LogWarning("Scan requested for shelf {ShelfId}, but it no longer exists", shelfId);
            return Result.NotFound($"Shelf {shelfId} no longer exists.");
        }

        logger.LogInformation("Scan started for shelf {ShelfId} '{Name}' ({FolderCount} folder(s))",
            shelfId, shelf.Name, shelf.Folders.Count);

        progressTracker.Start(shelfId);
        try
        {
            return await ScanInternalAsync(shelf, shelfId, sw, cancellationToken);
        }
        finally
        {
            progressTracker.Finish(shelfId);
        }
    }

    private async Task<Result<ScanResult>> ScanInternalAsync(Shelf shelf, int shelfId, Stopwatch sw, CancellationToken cancellationToken)
    {
        int filesScanned = 0;
        int booksAdded = 0;
        int booksUpdated = 0;
        int booksRemoved = 0;
        int errors = 0;

        var supportedExtensions = new HashSet<string>(extractorFactory.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
        var seenFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Must canonicalize paths: OrdinalIgnoreCase does not treat "D:/a.epub" and "D:\a.epub"
        // as equal. Without this, the same file can be inserted twice, orphans mis-detected, and
        // metadata/covers end up on the wrong row.
        var existingByPath = (await bookRepository.FindAsync(new SearchOptions<Book>
        {
            Query = b => b.ShelfId == shelfId,
        })).ToDictionary(b => CanonicalFilePath(b.FilePath), StringComparer.OrdinalIgnoreCase);

        var authorCache = new Dictionary<string, Author>(StringComparer.OrdinalIgnoreCase);
        var seriesCache = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
        var genreCache = new Dictionary<string, Genre>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in shelf.Folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(folder.Path))
            {
                logger.LogWarning("Shelf {ShelfId} folder '{Path}' does not exist; skipping", shelfId, folder.Path);
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder.Path, "*.*", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to enumerate folder {Path}", folder.Path);
                errors++;
                continue;
            }

            foreach (string filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!supportedExtensions.Contains(Path.GetExtension(filePath)))
                {
                    continue;
                }

                filesScanned++;
                string canonicalFilePath = CanonicalFilePath(filePath);
                seenFilePaths.Add(canonicalFilePath);

                try
                {
                    var (added, updated) = await ProcessFileAsync(
                        shelfId,
                        canonicalFilePath,
                        existingByPath,
                        authorCache,
                        seriesCache,
                        genreCache,
                        cancellationToken);

                    if (added)
                    {
                        booksAdded++;
                    }

                    if (updated)
                    {
                        booksUpdated++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Failed to process file {FilePath}", filePath);
                    errors++;
                }

                progressTracker.Update(shelfId, p =>
                {
                    p.FilesScanned = filesScanned;
                    p.BooksAdded = booksAdded;
                    p.BooksUpdated = booksUpdated;
                    p.Errors = errors;
                    p.CurrentFile = Path.GetFileName(canonicalFilePath);
                });
            }
        }

        // Remove books whose files no longer exist (and aren't in any other folder).
        var orphans = existingByPath
            .Where(kvp => !seenFilePaths.Contains(kvp.Key))
            .Select(kvp => kvp.Value)
            .ToList();

        foreach (var orphan in orphans)
        {
            storage.DeleteCover(orphan.CoverImagePath);
        }

        if (orphans.Count > 0)
        {
            await bookRepository.DeleteAsync(orphans);
            booksRemoved = orphans.Count;
            progressTracker.Update(shelfId, p => p.BooksRemoved = booksRemoved);
        }

        shelf.LastScannedAt = DateTime.UtcNow;
        await shelfRepository.UpdateAsync(shelf);

        sw.Stop();

        var result = new ScanResult
        {
            FilesScanned = filesScanned,
            BooksAdded = booksAdded,
            BooksUpdated = booksUpdated,
            BooksRemoved = booksRemoved,
            Errors = errors,
            Duration = sw.Elapsed,
        };

        logger.LogInformation(
            "Scan finished for shelf {ShelfId} in {Duration:c}: {Files} files, +{Added}/~{Updated}/-{Removed} books, {Errors} error(s)",
            shelfId, result.Duration, result.FilesScanned, result.BooksAdded, result.BooksUpdated, result.BooksRemoved, result.Errors);

        return Result.Success(result);
    }

    private async Task<(bool Added, bool Updated)> ProcessFileAsync(
        int shelfId,
        string filePath,
        Dictionary<string, Book> existingByPath,
        Dictionary<string, Author> authorCache,
        Dictionary<string, Series> seriesCache,
        Dictionary<string, Genre> genreCache,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var extractor = extractorFactory.GetFor(filePath);
        if (extractor is null)
        {
            return (false, false);
        }

        bool exists = existingByPath.TryGetValue(filePath, out var book);

        if (exists && book is not null
            && book.FileSizeBytes == fileInfo.Length
            && book.FileLastModified is { } prevModified
            && Math.Abs((prevModified - fileInfo.LastWriteTimeUtc).TotalSeconds) < 1
            && !string.IsNullOrEmpty(book.CoverImagePath))
        {
            // Up to date — nothing to do.
            return (false, false);
        }

        // Cover-only backfill: if file metadata matches but we never saved a cover (typically
        // older PDF entries scanned before cover rendering was supported), only re-extract the
        // cover instead of rewriting all metadata.
        if (exists && book is not null
            && book.FileSizeBytes == fileInfo.Length
            && book.FileLastModified is { } prev2
            && Math.Abs((prev2 - fileInfo.LastWriteTimeUtc).TotalSeconds) < 1
            && string.IsNullOrEmpty(book.CoverImagePath))
        {
            var coverOnly = await extractor.ExtractAsync(filePath, cancellationToken);
            if (coverOnly.Cover is not null)
            {
                await SaveCoverAsync(book, coverOnly.Cover, cancellationToken);
                return (false, true);
            }
            return (false, false);
        }

        var metadata = await extractor.ExtractAsync(filePath, cancellationToken);

        if (book is null)
        {
            book = new Book
            {
                ShelfId = shelfId,
                FilePath = filePath,
                FileFormat = extractor.Format,

                Title = !string.IsNullOrEmpty(metadata.Title)
                    ? metadata.Title.ToLower().Humanize(LetterCasing.Title)
                    : "Unknown Title",

                FileSizeBytes = fileInfo.Length,
                FileLastModified = fileInfo.LastWriteTimeUtc,
                LastScannedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };
            ApplyMetadata(book, metadata);
            book = await bookRepository.InsertAsync(book);
            existingByPath[filePath] = book;

            await SyncAuthorsAsync(book.Id, metadata.AuthorNames, authorCache);
            await SyncGenresAsync(book.Id, metadata.Genres, genreCache);
            await ResolveSeriesAsync(book, metadata.SeriesName, seriesCache);
            await SaveCoverAsync(book, metadata.Cover, cancellationToken);

            return (true, false);
        }
        else
        {
            book.FilePath = filePath;
            book.FileSizeBytes = fileInfo.Length;
            book.FileLastModified = fileInfo.LastWriteTimeUtc;
            book.FileFormat = extractor.Format;
            book.LastScannedAt = DateTime.UtcNow;
            ApplyMetadata(book, metadata);

            await ResolveSeriesAsync(book, metadata.SeriesName, seriesCache);
            await bookRepository.UpdateAsync(book);

            await SyncAuthorsAsync(book.Id, metadata.AuthorNames, authorCache);
            await SyncGenresAsync(book.Id, metadata.Genres, genreCache);
            await SaveCoverAsync(book, metadata.Cover, cancellationToken);

            return (false, true);
        }
    }

    private static void ApplyMetadata(Book book, EbookMetadata metadata)
    {
        book.Title ??= metadata.Title;
        book.SortTitle ??= BuildSortTitle(metadata.Title);
        book.Subtitle ??= metadata.Subtitle;
        book.Description ??= metadata.Description;
        book.Language ??= Truncate(metadata.Language, 16);
        book.Publisher ??= Truncate(metadata.Publisher, 256);
        book.Isbn ??= Truncate(metadata.Isbn, 32);
        book.PublishedOn ??= metadata.PublishedOn;
        book.PageCount ??= metadata.PageCount;
        book.NumberInSeries ??= metadata.NumberInSeries;
    }

    private async Task SyncAuthorsAsync(int bookId, IReadOnlyList<string> authorNames, Dictionary<string, Author> cache)
    {
        var existing = (await bookAuthorRepository.FindAsync(new SearchOptions<BookAuthor>
        {
            Query = ba => ba.BookId == bookId,
        })).ToList();

        if (authorNames.Count == 0)
        {
            if (existing.Count > 0)
            {
                await bookAuthorRepository.DeleteAsync(existing);
            }

            return;
        }

        var resolved = new List<Author>(authorNames.Count);
        foreach (string raw in authorNames)
        {
            string name = raw.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            string key = name.ToLowerInvariant();
            if (!cache.TryGetValue(key, out var author))
            {
                author = await authorRepository.FindOneAsync(new SearchOptions<Author>
                {
                    Query = a => a.NormalizedName == key,
                });

                author ??= await authorRepository.InsertAsync(new Author
                {
                    Name = name,
                    NormalizedName = key,
                });
                cache[key] = author;
            }
            resolved.Add(author);
        }

        var desiredIds = resolved.Select(a => a.Id).Distinct().ToList();
        var existingIds = existing.Select(ba => ba.AuthorId).ToHashSet();

        var toRemove = existing.Where(ba => !desiredIds.Contains(ba.AuthorId)).ToList();
        if (toRemove.Count > 0)
        {
            await bookAuthorRepository.DeleteAsync(toRemove);
        }

        var toAdd = desiredIds
            .Where(id => !existingIds.Contains(id))
            .Select((id, idx) => new BookAuthor { BookId = bookId, AuthorId = id, Position = idx })
            .ToList();

        if (toAdd.Count > 0)
        {
            await bookAuthorRepository.InsertAsync(toAdd);
        }
    }

    private async Task SyncGenresAsync(int bookId, IReadOnlyList<string> genreNames, Dictionary<string, Genre> cache)
    {
        var existing = (await bookGenreRepository.FindAsync(new SearchOptions<BookGenre>
        {
            Query = bg => bg.BookId == bookId,
        })).ToList();

        if (genreNames.Count == 0)
        {
            if (existing.Count > 0)
            {
                await bookGenreRepository.DeleteAsync(existing);
            }

            return;
        }

        var resolved = new List<Genre>(genreNames.Count);
        foreach (string raw in genreNames)
        {
            string name = raw.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            string key = name.ToLowerInvariant();
            if (!cache.TryGetValue(key, out var genre))
            {
                genre = await genreRepository.FindOneAsync(new SearchOptions<Genre>
                {
                    Query = g => g.NormalizedName == key,
                });

                genre ??= await genreRepository.InsertAsync(new Genre
                {
                    Name = name,
                    NormalizedName = key,
                });
                cache[key] = genre;
            }
            resolved.Add(genre);
        }

        var desiredIds = resolved.Select(g => g.Id).Distinct().ToList();
        var existingIds = existing.Select(bg => bg.GenreId).ToHashSet();

        var toRemove = existing.Where(bg => !desiredIds.Contains(bg.GenreId)).ToList();
        if (toRemove.Count > 0)
        {
            await bookGenreRepository.DeleteAsync(toRemove);
        }

        var toAdd = desiredIds
            .Where(id => !existingIds.Contains(id))
            .Select(id => new BookGenre { BookId = bookId, GenreId = id })
            .ToList();
        if (toAdd.Count > 0)
        {
            await bookGenreRepository.InsertAsync(toAdd);
        }
    }

    private async Task ResolveSeriesAsync(Book book, string? seriesName, Dictionary<string, Series> cache)
    {
        if (string.IsNullOrWhiteSpace(seriesName))
        {
            book.SeriesId = null;
            return;
        }

        string trimmed = seriesName.Trim();
        string key = trimmed.ToLowerInvariant();

        if (!cache.TryGetValue(key, out var series))
        {
            series = await seriesRepository.FindOneAsync(new SearchOptions<Series>
            {
                Query = s => s.NormalizedName == key,
            });

            series ??= await seriesRepository.InsertAsync(new Series
            {
                Name = trimmed,
                NormalizedName = key,
            });
            cache[key] = series;
        }

        book.SeriesId = series.Id;
    }

    private async Task SaveCoverAsync(Book book, EbookCoverImage? cover, CancellationToken cancellationToken)
    {
        if (cover is null)
        {
            return;
        }

        string relativePath = await storage.SaveCoverAsync(book.Id, cover, cancellationToken);
        book.CoverImagePath = relativePath;
        await bookRepository.UpdateAsync(book);
    }

    private static string CanonicalFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string? Truncate(string? value, int max) => string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max];

    private static string BuildSortTitle(string title)
    {
        string trimmed = title.Trim();
        if (trimmed.Length == 0)
        {
            return title;
        }

        // Ignore leading articles for sort-title (e.g. "The Hobbit" -> "Hobbit").
        string[] articles = ["a ", "an ", "the "];
        string lower = trimmed.ToLowerInvariant();
        foreach (string article in articles)
        {
            if (!lower.StartsWith(article, StringComparison.Ordinal))
            {
                continue;
            }

            string withoutArticle = trimmed[article.Length..].TrimStart();
            return withoutArticle.Length == 0 ? trimmed : withoutArticle;
        }

        return trimmed;
    }
}
