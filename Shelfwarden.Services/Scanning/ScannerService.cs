using System.Diagnostics;
using System.Text.Json;
using Humanizer;
using Shelfwarden.Models.Metadata;
using Shelfwarden.Services.Metadata;
using Shelfwarden.Services.Storage;

namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Recursive shelf scanner. For every supported file under the shelf's folders this:
///   1. Looks up the existing <see cref="Book"/> by <c>(ShelfId, FilePath)</c>.
///   2. Skips it when <see cref="FileInfo.LastWriteTimeUtc"/> + size match the stored values.
///   3. Otherwise extracts metadata (via <see cref="IEbookMetadataExtractor"/>), persists/updates
///      the book + author/series/genre/tag relations, and writes the cover bytes to disk.
/// Optional per-folder <c>shelfwarden_import.json</c> sidecars (nearest ancestor wins) merge or replace
/// authors, genres, and tags after extraction; <c>series</c> is an optional plain string override.
/// A newer sidecar timestamp than the book's last
/// scan forces metadata to be reapplied even when the ebook file has not changed.
/// At the end it removes books whose files have disappeared and stamps <see cref="Shelf.LastScannedAt"/>.
/// </summary>
public sealed class ScannerService(
    ILogger<ScannerService> logger,
    IEbookMetadataExtractorFactory extractorFactory,
    ICalibreOpfReader calibreOpfReader,
    IBookMetadataService onlineMetadataService,
    IStoragePathProvider storage,
    IScanProgressTracker progressTracker,
    IRepository<Shelf> shelfRepository,
    IRepository<Book> bookRepository,
    IRepository<Author> authorRepository,
    IRepository<BookAuthor> bookAuthorRepository,
    IRepository<Series> seriesRepository,
    IRepository<Genre> genreRepository,
    IRepository<BookGenre> bookGenreRepository,
    IRepository<Tag> tagRepository,
    IRepository<BookTag> bookTagRepository,
    IRepository<Collection> collectionRepository,
    IRepository<CollectionBook> collectionBookRepository) : IScannerService
{
    private readonly Dictionary<string, (long LastWriteTicks, ShelfwardenImportOverlay? Overlay)> _importOverlayCache =
        new(StringComparer.OrdinalIgnoreCase);

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
        var tagCache = new Dictionary<string, Tag>(StringComparer.OrdinalIgnoreCase);
        var collectionCache = new Dictionary<string, Collection>(StringComparer.OrdinalIgnoreCase);

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

                if (!ShelfScanFileFilter.ShouldInclude(filePath))
                {
                    continue;
                }

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
                        shelf,
                        canonicalFilePath,
                        folder.Path,
                        existingByPath,
                        authorCache,
                        seriesCache,
                        genreCache,
                        tagCache,
                        collectionCache,
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
        Shelf shelf,
        string filePath,
        string shelfFolderRoot,
        Dictionary<string, Book> existingByPath,
        Dictionary<string, Author> authorCache,
        Dictionary<string, Series> seriesCache,
        Dictionary<string, Genre> genreCache,
        Dictionary<string, Tag> tagCache,
        Dictionary<string, Collection> collectionCache,
        CancellationToken cancellationToken)
    {
        int shelfId = shelf.Id;
        bool isCalibre = shelf.DirectoryStructure == DirectoryStructure.Calibre;
        var fileInfo = new FileInfo(filePath);
        var extractor = extractorFactory.GetFor(filePath);
        if (extractor is null)
        {
            return (false, false);
        }

        bool exists = existingByPath.TryGetValue(filePath, out var book);
        bool importNewer = exists && book is not null
            && (ShouldForceRescanDueToImport(filePath, shelfFolderRoot, book)
                || (isCalibre && ShouldForceRescanDueToCalibreOpf(filePath, book)));

        if (exists && book is not null
            && book.FileSizeBytes == fileInfo.Length
            && book.FileLastModified is { } prevModified
            && Math.Abs((prevModified - fileInfo.LastWriteTimeUtc).TotalSeconds) < 1
            && !string.IsNullOrEmpty(book.CoverImagePath)
            && !importNewer)
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
            && string.IsNullOrEmpty(book.CoverImagePath)
            && !importNewer)
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

        // For Calibre shelves the sibling metadata.opf is the authoritative metadata source; fold
        // it over whatever we extracted from the file itself before the sidecar overlay applies.
        if (isCalibre)
        {
            var opf = await calibreOpfReader.TryReadAsync(filePath, cancellationToken);
            if (opf is not null)
            {
                metadata = MergeCalibreMetadata(metadata, opf);
            }
        }

        string? importJsonPath = ShelfwardenImportPath.FindNearestImportJsonPath(filePath, shelfFolderRoot);
        var importOverlay = importJsonPath is null
            ? null
            : await TryLoadImportOverlayAsync(importJsonPath, cancellationToken);
        bool hasImportOverlay = importOverlay is not null;

        // Shelf options are defaults; shelfwarden_import.json sidecars override them.
        var authorNames = metadata.AuthorNames;
        var genreNames = metadata.Genres;
        var tagNames = metadata.Tags;
        string? seriesName = metadata.SeriesName;
        bool useFileNameForTitle = shelf.AlwaysUseFileNameForTitle;

        if (shelf.AlwaysIgnoreAuthor)
        {
            authorNames = [];
        }

        if (shelf.AlwaysIgnoreGenres)
        {
            genreNames = [];
        }

        if (shelf.AlwaysIgnoreTags)
        {
            tagNames = [];
        }

        string? collectionName = null;
        if (!exists && shelf.AssignNewBooksToCollection)
        {
            collectionName = string.IsNullOrWhiteSpace(shelf.NewBooksCollectionName)
                ? Constants.DefaultNewBooksCollectionName
                : shelf.NewBooksCollectionName.Trim();
        }

        if (importOverlay is not null)
        {
            authorNames = ShelfwardenImportMerger.MergeList(authorNames, importOverlay.Author);
            genreNames = ShelfwardenImportMerger.MergeList(genreNames, importOverlay.Genres);
            tagNames = ShelfwardenImportMerger.MergeList(tagNames, importOverlay.Tags);
            seriesName = ShelfwardenImportMerger.MergeSeries(seriesName, importOverlay.Series);
            useFileNameForTitle = ShelfwardenImportMerger.ResolveUseFileNameForTitle(
                useFileNameForTitle, importOverlay.UseFileNameForTitle);

            string? sidecarCollection = ShelfwardenImportMerger.MergeCollection(importOverlay.Collection);
            if (sidecarCollection is not null)
            {
                collectionName = sidecarCollection;
            }
        }

        string resolvedTitle = ResolveImportedTitle(filePath, metadata.Title, useFileNameForTitle);

        // Optionally back-fill anything still missing from online sources (Google Books / Open
        // Library). Best-effort: a failed lookup never fails the scan, and we only fill gaps.
        if (shelf.AutoFetchOnlineMetadata)
        {
            (metadata, authorNames, genreNames, tagNames, seriesName) = await EnrichFromOnlineAsync(
                shelf, resolvedTitle, metadata, authorNames, genreNames, tagNames, seriesName, cancellationToken);
        }

        if (book is null)
        {
            book = new Book
            {
                ShelfId = shelfId,
                FilePath = filePath,
                FileFormat = extractor.Format,
                Title = resolvedTitle,
                SortTitle = resolvedTitle.ToSortTitle().Truncate(MaxBookTitleFieldLength),

                FileSizeBytes = fileInfo.Length,
                FileLastModified = fileInfo.LastWriteTimeUtc,
                LastScannedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };
            ApplyMetadata(book, metadata);
            if (hasImportOverlay)
            {
                // Sidecar-driven metadata counts as reviewed/import-curated content.
                book.UpdatedAt = DateTime.UtcNow;
            }
            book = await bookRepository.InsertAsync(book);
            existingByPath[filePath] = book;

            await SyncAuthorsAsync(book.Id, authorNames, authorCache);
            await SyncGenresAsync(book.Id, genreNames, genreCache);
            await SyncTagsAsync(book.Id, tagNames, tagCache);
            await ResolveSeriesAsync(book, seriesName, seriesCache);
            await SyncCollectionAsync(book.Id, collectionName, collectionCache);
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
            if (useFileNameForTitle)
            {
                book.Title = resolvedTitle;
                book.SortTitle = resolvedTitle.ToSortTitle().Truncate(MaxBookTitleFieldLength);
            }

            ApplyMetadata(book, metadata);
            if (hasImportOverlay)
            {
                // Keep sidecar-imported books out of "Awaiting Review" filters.
                book.UpdatedAt = DateTime.UtcNow;
            }

            await ResolveSeriesAsync(book, seriesName, seriesCache);
            await bookRepository.UpdateAsync(book);

            await SyncAuthorsAsync(book.Id, authorNames, authorCache);
            await SyncGenresAsync(book.Id, genreNames, genreCache);
            await SyncTagsAsync(book.Id, tagNames, tagCache);
            await SyncCollectionAsync(book.Id, collectionName, collectionCache);
            await SaveCoverAsync(book, metadata.Cover, cancellationToken);

            return (false, true);
        }
    }

    /// <summary>
    /// Folds Calibre <c>metadata.opf</c> values over the metadata extracted from the file. OPF values
    /// win whenever present; the file's values (notably page count and any embedded cover) fill the gaps.
    /// Calibre stores user tags in <c>&lt;dc:subject&gt;</c>, so those map onto <see cref="EbookMetadata.Tags"/>.
    /// </summary>
    private static EbookMetadata MergeCalibreMetadata(EbookMetadata fromFile, CalibreOpfMetadata opf) => fromFile with
    {
        Title = string.IsNullOrWhiteSpace(opf.Title) ? fromFile.Title : opf.Title.Trim(),
        Description = string.IsNullOrWhiteSpace(opf.Description) ? fromFile.Description : opf.Description,
        Language = string.IsNullOrWhiteSpace(opf.Language) ? fromFile.Language : opf.Language,
        Publisher = string.IsNullOrWhiteSpace(opf.Publisher) ? fromFile.Publisher : opf.Publisher,
        Isbn = string.IsNullOrWhiteSpace(opf.Isbn) ? fromFile.Isbn : opf.Isbn,
        PublishedOn = opf.PublishedOn ?? fromFile.PublishedOn,
        AuthorNames = opf.AuthorNames.Count > 0 ? opf.AuthorNames : fromFile.AuthorNames,
        Tags = opf.Tags.Count > 0 ? opf.Tags : fromFile.Tags,
        // Calibre has no genre concept (its "tags" map onto dc:subject above); drop any genres the
        // embedded file declared so a Calibre book isn't tagged and genre-d with the same strings.
        Genres = [],
        SeriesName = string.IsNullOrWhiteSpace(opf.SeriesName) ? fromFile.SeriesName : opf.SeriesName,
        NumberInSeries = opf.NumberInSeries ?? fromFile.NumberInSeries,
        Cover = opf.Cover ?? fromFile.Cover,
    };

    private bool ShouldForceRescanDueToCalibreOpf(string filePath, Book book)
    {
        string? opfPath = calibreOpfReader.FindOpfPath(filePath);
        if (opfPath is null)
        {
            return false;
        }

        try
        {
            var opfTime = new FileInfo(opfPath).LastWriteTimeUtc;
            var bookStamp = book.LastScannedAt ?? book.CreatedAt;
            return opfTime > bookStamp;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read Calibre metadata.opf timestamp next to {Path}", filePath);
            return false;
        }
    }

    private bool ShouldForceRescanDueToImport(string filePath, string shelfFolderRoot, Book book)
    {
        string? importPath = ShelfwardenImportPath.FindNearestImportJsonPath(filePath, shelfFolderRoot);
        if (importPath is null)
        {
            return false;
        }

        try
        {
            var importTime = new FileInfo(importPath).LastWriteTimeUtc;
            var bookStamp = book.LastScannedAt ?? book.CreatedAt;
            return importTime > bookStamp;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read import file timestamp {Path}", importPath);
            return false;
        }
    }

    private async Task<ShelfwardenImportOverlay?> TryLoadImportOverlayAsync(string importPath, CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(importPath);
            if (!info.Exists)
            {
                return null;
            }

            long ticks = info.LastWriteTimeUtc.Ticks;
            if (_importOverlayCache.TryGetValue(importPath, out var entry) && entry.LastWriteTicks == ticks)
            {
                return entry.Overlay;
            }

            string text = await File.ReadAllTextAsync(importPath, cancellationToken);
            var overlay = JsonSerializer.Deserialize<ShelfwardenImportOverlay>(text, ShelfwardenImportJson.Options);
            _importOverlayCache[importPath] = (ticks, overlay);
            return overlay;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid or unreadable shelf import sidecar {Path}", importPath);
            return null;
        }
    }

    /// <summary>
    /// Queries online metadata sources and folds any returned values into the still-empty fields of
    /// the local metadata + relation lists. Scalar fields use <c>??=</c> semantics (only fill nulls);
    /// author/genre/tag lists are only populated when currently empty and not suppressed by the
    /// shelf's "always ignore" flags. Title is never overwritten — it's already resolved locally.
    /// </summary>
    private async Task<(EbookMetadata Metadata, IReadOnlyList<string> Authors, IReadOnlyList<string> Genres, IReadOnlyList<string> Tags, string? SeriesName)> EnrichFromOnlineAsync(
        Shelf shelf,
        string resolvedTitle,
        EbookMetadata metadata,
        IReadOnlyList<string> authorNames,
        IReadOnlyList<string> genreNames,
        IReadOnlyList<string> tagNames,
        string? seriesName,
        CancellationToken cancellationToken)
    {
        var query = new BookMetadataQuery
        {
            Title = string.IsNullOrWhiteSpace(metadata.Title) ? resolvedTitle : metadata.Title,
            Author = authorNames.FirstOrDefault(),
            Isbn = metadata.Isbn,
            Limit = 5,
        };

        if (query.IsEmpty)
        {
            return (metadata, authorNames, genreNames, tagNames, seriesName);
        }

        ExternalBookMetadataDto? match;
        try
        {
            match = await onlineMetadataService.FindBestMatchAsync(query, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Online metadata enrichment failed for '{Title}' on shelf {ShelfId}", query.Title, shelf.Id);
            return (metadata, authorNames, genreNames, tagNames, seriesName);
        }

        if (match is null)
        {
            return (metadata, authorNames, genreNames, tagNames, seriesName);
        }

        var enriched = metadata with
        {
            Subtitle = metadata.Subtitle ?? match.Subtitle,
            Description = metadata.Description ?? match.Description,
            Language = metadata.Language ?? match.Language,
            Publisher = metadata.Publisher ?? match.Publisher,
            Isbn = metadata.Isbn ?? match.Isbn,
            PublishedOn = metadata.PublishedOn ?? match.PublishedOn,
            PageCount = metadata.PageCount ?? match.PageCount,
            NumberInSeries = metadata.NumberInSeries ?? match.NumberInSeries,
        };

        if (authorNames.Count == 0 && !shelf.AlwaysIgnoreAuthor && match.Authors.Count > 0)
        {
            authorNames = match.Authors;
        }

        if (genreNames.Count == 0 && !shelf.AlwaysIgnoreGenres && match.Genres.Count > 0)
        {
            genreNames = match.Genres;
        }

        if (tagNames.Count == 0 && !shelf.AlwaysIgnoreTags && match.Tags.Count > 0)
        {
            tagNames = match.Tags;
        }

        if (string.IsNullOrWhiteSpace(seriesName) && !string.IsNullOrWhiteSpace(match.SeriesName))
        {
            seriesName = match.SeriesName;
        }

        logger.LogDebug("Enriched '{Title}' from {Provider} during scan of shelf {ShelfId}", resolvedTitle, match.Provider, shelf.Id);
        return (enriched, authorNames, genreNames, tagNames, seriesName);
    }

    private static void ApplyMetadata(Book book, EbookMetadata metadata)
    {
        book.Title ??= metadata.Title.Truncate(MaxBookTitleFieldLength);
        book.SortTitle ??= metadata.Title.ToSortTitle().Truncate(MaxBookTitleFieldLength);
        book.Subtitle ??= metadata.Subtitle.Truncate(MaxBookTitleFieldLength);
        book.Description ??= metadata.Description;
        book.Language ??= metadata.Language.Truncate(16);
        book.Publisher ??= metadata.Publisher.Truncate(256);
        book.Isbn ??= metadata.Isbn.Truncate(32);
        book.PublishedOn ??= metadata.PublishedOn;
        book.PageCount ??= metadata.PageCount;
        book.NumberInSeries ??= metadata.NumberInSeries;
    }

    private static string ResolveImportedTitle(string filePath, string? metadataTitle, bool useFileNameForTitle)
    {
        if (useFileNameForTitle)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName.Truncate(MaxBookTitleFieldLength);
            }
        }

        return !string.IsNullOrWhiteSpace(metadataTitle)
            ? metadataTitle.ToLower().Humanize(LetterCasing.Title).Truncate(MaxBookTitleFieldLength)
            : "Unknown Title";
    }

    private async Task SyncAuthorsAsync(int bookId, IReadOnlyList<string> authorNames, IDictionary<string, Author> cache)
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

    private async Task SyncTagsAsync(int bookId, IReadOnlyList<string> tagNames, Dictionary<string, Tag> cache)
    {
        var existing = (await bookTagRepository.FindAsync(new SearchOptions<BookTag>
        {
            Query = bt => bt.BookId == bookId,
        })).ToList();

        if (tagNames.Count == 0)
        {
            if (existing.Count > 0)
            {
                await bookTagRepository.DeleteAsync(existing);
            }

            return;
        }

        var resolved = new List<Tag>(tagNames.Count);
        foreach (string raw in tagNames)
        {
            string name = raw.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            string key = name.ToLowerInvariant();
            if (!cache.TryGetValue(key, out var tag))
            {
                tag = await tagRepository.FindOneAsync(new SearchOptions<Tag>
                {
                    Query = t => t.NormalizedName == key,
                });

                tag ??= await tagRepository.InsertAsync(new Tag
                {
                    Name = name,
                    NormalizedName = key,
                });
                cache[key] = tag;
            }

            resolved.Add(tag);
        }

        var desiredIds = resolved.Select(t => t.Id).Distinct().ToList();
        var existingIds = existing.Select(bt => bt.TagId).ToHashSet();

        var toRemove = existing.Where(bt => !desiredIds.Contains(bt.TagId)).ToList();
        if (toRemove.Count > 0)
        {
            await bookTagRepository.DeleteAsync(toRemove);
        }

        var toAdd = desiredIds
            .Where(id => !existingIds.Contains(id))
            .Select(id => new BookTag { BookId = bookId, TagId = id })
            .ToList();
        if (toAdd.Count > 0)
        {
            await bookTagRepository.InsertAsync(toAdd);
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
        string key = trimmed.ToSortTitle().ToLowerInvariant();

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

    private async Task SyncCollectionAsync(int bookId, string? collectionName, Dictionary<string, Collection> cache)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            return;
        }

        var collection = await ResolveCollectionAsync(collectionName.Trim(), cache);
        var existing = await collectionBookRepository.FindOneAsync(new SearchOptions<CollectionBook>
        {
            Query = cb => cb.BookId == bookId && cb.CollectionId == collection.Id,
        });
        if (existing is not null)
        {
            return;
        }

        await collectionBookRepository.InsertAsync(new CollectionBook
        {
            BookId = bookId,
            CollectionId = collection.Id,
        });
    }

    private async Task<Collection> ResolveCollectionAsync(string collectionName, Dictionary<string, Collection> cache)
    {
        string key = collectionName.ToLowerInvariant();
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var collection = await collectionRepository.FindOneAsync(new SearchOptions<Collection>
        {
            Query = c => c.OwnerUserId == Constants.GlobalUserId && c.Name == collectionName,
        });

        collection ??= await collectionRepository.InsertAsync(new Collection
        {
            Name = collectionName,
            OwnerUserId = Constants.GlobalUserId,
            CreatedAt = DateTime.UtcNow,
        });

        cache[key] = collection;
        return collection;
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

    private const int MaxBookTitleFieldLength = 512;
}