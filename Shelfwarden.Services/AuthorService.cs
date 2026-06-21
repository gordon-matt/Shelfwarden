using System.Text.Json;
using OpenLibraryNET.Loader;
using OpenLibraryNET.Utility;
using Shelfwarden.Services.Metadata;
using Shelfwarden.Services.Storage;

namespace Shelfwarden.Services;

public class AuthorService(
    ILogger<AuthorService> logger,
    IRepository<Author> authorRepository,
    IRepository<AuthorLink> authorLinkRepository,
    IRepository<Book> bookRepository,
    IRepository<BookAuthor> bookAuthorRepository,
    IRepository<BookProgress> progressRepository,
    IRepository<AdditionalContentItem> contentRepository,
    IUserContextService userContext,
    IHttpClientFactory httpClientFactory,
    IStoragePathProvider storagePathProvider) : IAuthorService
{
    public async Task<Result<IReadOnlyList<AuthorDto>>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default)
    {
        var options = new SearchOptions<Author>
        {
            PageNumber = 1,
            PageSize = Math.Clamp(limit, 1, 200),
            OrderBy = query => query.OrderBy(a => a.NormalizedName),
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            string needle = query.Trim().ToLowerInvariant();
            options.Query = a => a.NormalizedName.Contains(needle);
        }

        var authors = await authorRepository.FindAsync(options);
        IReadOnlyList<AuthorDto> result = authors
            .Select(a => new AuthorDto(a.Id, a.Name, a.Biography))
            .ToList();
        return Result.Success(result);
    }

    public async Task<Result<AuthorDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var author = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.Id == id,
        });
        return author is null ? (Result<AuthorDto>)Result.NotFound() : Result.Success(new AuthorDto(author.Id, author.Name, author.Biography));
    }

    public async Task<Result<AuthorDto>> GetOrCreateAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Invalid(new ValidationError(nameof(name), "Name is required."));
        }

        string trimmed = name.Trim();
        string normalised = trimmed.ToLowerInvariant();

        var existing = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.NormalizedName == normalised,
        });
        if (existing is not null)
        {
            return Result.Success(new AuthorDto(existing.Id, existing.Name, existing.Biography));
        }

        var created = await authorRepository.InsertAsync(new Author
        {
            Name = trimmed,
            NormalizedName = normalised,
        });
        return Result.Success(new AuthorDto(created.Id, created.Name, created.Biography));
    }

    public async Task<Result<IReadOnlyList<AuthorListItemDto>>> ListAsync(string? query = null, int? shelfId = null, CancellationToken cancellationToken = default)
    {
        var options = new SearchOptions<Author>
        {
            OrderBy = query => query.OrderBy(a => a.NormalizedName),
            CancellationToken = cancellationToken,
        };

        bool hasSearch = !string.IsNullOrWhiteSpace(query);
        string needle = hasSearch ? query!.Trim().ToLowerInvariant() : string.Empty;

        if (shelfId is int sid)
        {
            options.Query = hasSearch
                ? a => EF.Functions.Like(a.NormalizedName, $"%{needle}%") &&
                       a.BookAuthors.Any(ba => ba.Book.ShelfId == sid)
                : a => a.BookAuthors.Any(ba => ba.Book.ShelfId == sid);
        }
        else if (hasSearch)
        {
            options.Query = a => EF.Functions.Like(a.NormalizedName, $"%{needle}%");
        }

        var authors = (await authorRepository.FindAsync(options)).ToList();
        var ids = authors.Select(a => a.Id).ToList();

        var countOptions = shelfId is int shelf
            ? new SearchOptions<BookAuthor>
            {
                Query = ba => ids.Contains(ba.AuthorId) && ba.Book.ShelfId == shelf,
                CancellationToken = cancellationToken,
            }
            : new SearchOptions<BookAuthor>
            {
                Query = ba => ids.Contains(ba.AuthorId),
                CancellationToken = cancellationToken,
            };

        // One join-table query → counts by author id. Cheaper than N round-trips.
        var counts = (await bookAuthorRepository.FindAsync(countOptions, ba => ba.AuthorId))
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        IReadOnlyList<AuthorListItemDto> result = authors
            .Select(a => new AuthorListItemDto(a.Id, a.Name, a.Biography, counts.GetValueOrDefault(a.Id, 0)))
            .Where(a => a.BookCount > 0)
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<AuthorDetailDto>> GetDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        var author = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.Id == id,
            Include = q => q
                .Include(a => a.PrimaryAuthor)
                .Include(a => a.Pseudonyms)
                .Include(a => a.Links),
            CancellationToken = cancellationToken,
        });
        if (author is null)
        {
            return Result.NotFound($"Author {id} not found.");
        }

        AuthorRefDto? primaryAuthor = author.PrimaryAuthor is { } primary
            ? new AuthorRefDto(primary.Id, primary.Name)
            : null;

        var pseudonyms = author.Pseudonyms
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new AuthorRefDto(p.Id, p.Name))
            .ToList();

        var links = author.Links
            .OrderBy(l => l.Name ?? l.Url, StringComparer.OrdinalIgnoreCase)
            .Select(l => new AuthorLinkDto(l.Id, l.Name, l.Url))
            .ToList();

        // Load every book for the author with everything we need to build BookListItemDto
        // entries. The author can have at most a few hundred books in any realistic scenario,
        // so a single Include is fine.
        var books = (await bookRepository.FindAsync(new SearchOptions<Book>
        {
            Query = b => b.BookAuthors.Any(ba => ba.AuthorId == id),
            Include = query => query
                .Include(b => b.Series)
                .Include(b => b.BookAuthors).ThenInclude(ba => ba.Author),
            OrderBy = query => query
                .OrderBy(b => b.SeriesId == null ? 1 : 0)
                .ThenBy(b => b.NumberInSeries)
                .ThenBy(b => b.SortTitle ?? b.Title),
            SplitQuery = true,
            CancellationToken = cancellationToken,
        })).ToList();

        return Result.Success(await BuildAuthorDetailDtoAsync(
            author.Id,
            author.Name,
            author.Biography,
            books,
            cancellationToken,
            primaryAuthor,
            pseudonyms,
            links));
    }

    public async Task<Result<int>> GetBooksWithoutAuthorsCountAsync(int? shelfId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            int count = shelfId is int sid
                ? await bookRepository.CountAsync(b => !b.BookAuthors.Any() && b.ShelfId == sid)
                : await bookRepository.CountAsync(b => !b.BookAuthors.Any());
            return Result.Success(count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to count books without authors");
            return Result.Error("Could not load author-less book count.");
        }
    }

    public async Task<Result<AuthorDetailDto>> GetUnknownAuthorDetailAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var books = (await bookRepository.FindAsync(new SearchOptions<Book>
            {
                Query = b => !b.BookAuthors.Any(),
                Include = query => query
                    .Include(b => b.Series)
                    .Include(b => b.BookAuthors).ThenInclude(ba => ba.Author),
                OrderBy = query => query
                    .OrderBy(b => b.SeriesId == null ? 1 : 0)
                    .ThenBy(b => b.NumberInSeries)
                    .ThenBy(b => b.SortTitle ?? b.Title),
                SplitQuery = true,
                CancellationToken = cancellationToken,
            })).ToList();

            return Result.Success(await BuildAuthorDetailDtoAsync(
                0,
                "Unknown",
                null,
                books,
                cancellationToken));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load books without authors");
            return Result.Error("Could not load books without authors.");
        }
    }

    private async Task<AuthorDetailDto> BuildAuthorDetailDtoAsync(
        int id,
        string displayName,
        string? biography,
        List<Book> books,
        CancellationToken cancellationToken,
        AuthorRefDto? primaryAuthor = null,
        IReadOnlyList<AuthorRefDto>? pseudonyms = null,
        IReadOnlyList<AuthorLinkDto>? links = null)
    {
        string? userId = userContext.GetCurrentUserId();
        var progressByBook = await BookProjections.LoadProgressPercentagesAsync(
            progressRepository, userId, books.Select(b => b.Id).ToList(), cancellationToken);

        var seriesGroups = books
            .Where(b => b.SeriesId.HasValue && b.Series is not null)
            .GroupBy(b => (b.SeriesId!.Value, b.Series!.Name))
            .Select(g => new AuthorSeriesGroupDto(
                g.Key.Value,
                g.Key.Name,
                g.Count(),
                g.OrderBy(b => b.NumberInSeries)
                    .Take(4)
                    .Select(b => new SeriesCoverDto(
                        b.Id,
                        b.CoverImagePath))
                    .ToList()))
            .OrderBy(s => s.SeriesName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var standalone = books
            .Where(b => !b.SeriesId.HasValue)
            .Select(b => BookProjections.ToListItem(b, progressByBook.GetValueOrDefault(b.Id)))
            .ToList();

        return new AuthorDetailDto(
            id,
            displayName,
            biography,
            books.Count,
            seriesGroups,
            standalone,
            primaryAuthor,
            pseudonyms ?? [],
            links ?? []);
    }

    public async Task<Result<int>> DeleteAuthorsAsync(IReadOnlyList<int> authorIds, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (authorIds is null || authorIds.Count == 0)
        {
            return Result.Invalid(new ValidationError(nameof(authorIds), "Select at least one author."));
        }

        var distinctIds = authorIds.Where(id => id > 0).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return Result.Invalid(new ValidationError(nameof(authorIds), "No valid author ids."));
        }

        try
        {
            // Pull all matching authors in a single round-trip and delete them as a batch.
            var entities = (await authorRepository.FindAsync(new SearchOptions<Author>
            {
                Query = a => distinctIds.Contains(a.Id),
                CancellationToken = cancellationToken,
            })).ToList();

            if (entities.Count == 0)
            {
                return Result.Success(0);
            }

            foreach (var entity in entities)
            {
                TryDeleteAuthorPhotoFile(entity.Id);
                // Leave content files in place (EF's SetNull keeps their FilePath valid);
                // just try to clean up the now-empty author folder.
                TryDeleteAuthorExtrasFolder(entity.Name, forceDelete: false);
            }

            await authorRepository.DeleteAsync(entities);

            logger.LogInformation("Administrator deleted {Count} author record(s) by id.", entities.Count);
            return Result.Success(entities.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete authors by id");
            return Result.Error("Could not delete the selected authors.");
        }
    }

    public async Task<Result> MergeAuthorsAsync(int primaryAuthorId, IReadOnlyList<int> otherAuthorIds, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (otherAuthorIds is null || otherAuthorIds.Count == 0)
        {
            return Result.Invalid(new ValidationError(nameof(otherAuthorIds), "Select at least one author to merge away."));
        }

        var others = otherAuthorIds.Where(id => id > 0 && id != primaryAuthorId).Distinct().ToList();
        if (others.Count == 0)
        {
            return Result.Invalid(new ValidationError(nameof(otherAuthorIds), "Nothing to merge."));
        }

        var primary = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.Id == primaryAuthorId,
            CancellationToken = cancellationToken,
        });
        if (primary is null)
        {
            return Result.NotFound($"Author {primaryAuthorId} was not found.");
        }

        try
        {
            // Pull every relevant link in a single query (links to either the primary OR any of
            // the others). We then partition them in-memory: any "other" link whose BookId is
            // already covered by the primary becomes a duplicate (delete); the rest get
            // re-pointed at the primary in one bulk update.
            var allLinks = (await bookAuthorRepository.FindAsync(new SearchOptions<BookAuthor>
            {
                Query = ba => ba.AuthorId == primaryAuthorId || others.Contains(ba.AuthorId),
                CancellationToken = cancellationToken,
            })).ToList();

            var primaryBookIds = allLinks
                .Where(ba => ba.AuthorId == primaryAuthorId)
                .Select(ba => ba.BookId)
                .ToHashSet();

            var otherLinks = allLinks.Where(ba => ba.AuthorId != primaryAuthorId).ToList();

            var duplicates = otherLinks.Where(ba => primaryBookIds.Contains(ba.BookId)).ToList();
            var toRepoint = otherLinks.Where(ba => !primaryBookIds.Contains(ba.BookId)).ToList();

            if (duplicates.Count > 0)
            {
                await bookAuthorRepository.DeleteAsync(duplicates);
            }

            if (toRepoint.Count > 0)
            {
                foreach (var ba in toRepoint)
                {
                    ba.AuthorId = primaryAuthorId;
                }
                await bookAuthorRepository.UpdateAsync(toRepoint);
            }

            // Migrate additional content items from the merged authors to the primary.
            var contentItems = (await contentRepository.FindAsync(new SearchOptions<AdditionalContentItem>
            {
                Query = ci => others.Contains(ci.AuthorId!.Value),
                CancellationToken = cancellationToken,
            })).ToList();

            if (contentItems.Count > 0)
            {
                string primaryFolderName = SanitizeFolderName(primary.Name);
                string primaryExtrasDir = Path.Combine(storagePathProvider.ExtrasDirectory, primaryFolderName);
                Directory.CreateDirectory(primaryExtrasDir);

                foreach (var ci in contentItems)
                {
                    ci.AuthorId = primaryAuthorId;

                    // Move the file into the primary author's folder (preserving any series subfolder).
                    string relativeToCurrent = "";
                    if (ci.AuthorId.HasValue)
                    {
                        // The file might be under _extras/{OtherAuthorName}/...
                        // Compute the part after the author folder.
                        string currentDir = Path.GetDirectoryName(ci.FilePath) ?? storagePathProvider.ExtrasDirectory;
                        string extrasRoot = storagePathProvider.ExtrasDirectory;
                        if (currentDir.StartsWith(extrasRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            string relative = currentDir[extrasRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            string[] parts = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
                            // parts[0] is the old author folder — skip it; parts[1..] are sub-folders (e.g. series).
                            if (parts.Length > 1)
                            {
                                relativeToCurrent = Path.Combine(parts[1..]);
                            }
                        }
                    }

                    string targetDir = string.IsNullOrEmpty(relativeToCurrent)
                        ? primaryExtrasDir
                        : Path.Combine(primaryExtrasDir, relativeToCurrent);
                    Directory.CreateDirectory(targetDir);

                    ci.FilePath = TryMoveFile(ci.FilePath, targetDir);
                }

                await contentRepository.UpdateAsync(contentItems);
            }

            // Drop the merged authors (and their photo files / extras folders) in one shot.
            var otherAuthors = (await authorRepository.FindAsync(new SearchOptions<Author>
            {
                Query = a => others.Contains(a.Id),
                CancellationToken = cancellationToken,
            })).ToList();

            foreach (var a in otherAuthors)
            {
                TryDeleteAuthorPhotoFile(a.Id);
                TryDeleteAuthorExtrasFolder(a.Name, forceDelete: true);
            }

            if (otherAuthors.Count > 0)
            {
                await authorRepository.DeleteAsync(otherAuthors);
            }

            logger.LogInformation(
                "Merged author ids {Others} into primary {PrimaryId}.",
                string.Join(',', others),
                primaryAuthorId);

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to merge authors into {PrimaryId}", primaryAuthorId);
            return Result.Error("Could not merge the selected authors.");
        }
    }

    public async Task<Result> LinkPseudonymAsync(int primaryAuthorId, int pseudonymAuthorId, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (primaryAuthorId == pseudonymAuthorId)
        {
            return Result.Invalid(new ValidationError(nameof(pseudonymAuthorId), "An author cannot be a pseudonym of itself."));
        }

        var authors = (await authorRepository.FindAsync(new SearchOptions<Author>
        {
            Query = a => a.Id == primaryAuthorId || a.Id == pseudonymAuthorId,
            Include = q => q.Include(a => a.Pseudonyms),
            CancellationToken = cancellationToken,
        })).ToList();

        var primary = authors.FirstOrDefault(a => a.Id == primaryAuthorId);
        var pseudonym = authors.FirstOrDefault(a => a.Id == pseudonymAuthorId);
        if (primary is null || pseudonym is null)
        {
            return Result.NotFound("One of the selected authors no longer exists.");
        }

        // Keep the relationship a single level deep: the chosen primary can't itself be a pseudonym,
        // and the pseudonym can't already be the primary of other authors.
        if (primary.PrimaryAuthorId is not null)
        {
            return Result.Invalid(new ValidationError(nameof(primaryAuthorId), "The primary author is itself a pseudonym. Link to the real author instead."));
        }

        if (pseudonym.Pseudonyms.Count > 0)
        {
            return Result.Invalid(new ValidationError(nameof(pseudonymAuthorId), "This author already has its own pseudonyms, so it can't become one."));
        }

        if (pseudonym.PrimaryAuthorId == primaryAuthorId)
        {
            return Result.Success();
        }

        try
        {
            pseudonym.PrimaryAuthorId = primaryAuthorId;
            await authorRepository.UpdateAsync(pseudonym);

            logger.LogInformation("Linked author {PseudonymId} as a pseudonym of {PrimaryId}.", pseudonymAuthorId, primaryAuthorId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to link pseudonym {PseudonymId} to {PrimaryId}", pseudonymAuthorId, primaryAuthorId);
            return Result.Error("Could not link the pseudonym.");
        }
    }

    public async Task<Result> UnlinkPseudonymAsync(int pseudonymAuthorId, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var pseudonym = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.Id == pseudonymAuthorId,
            CancellationToken = cancellationToken,
        });
        if (pseudonym is null)
        {
            return Result.NotFound($"Author {pseudonymAuthorId} not found.");
        }

        if (pseudonym.PrimaryAuthorId is null)
        {
            return Result.Success();
        }

        try
        {
            pseudonym.PrimaryAuthorId = null;
            await authorRepository.UpdateAsync(pseudonym);

            logger.LogInformation("Unlinked pseudonym {PseudonymId}.", pseudonymAuthorId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to unlink pseudonym {PseudonymId}", pseudonymAuthorId);
            return Result.Error("Could not unlink the pseudonym.");
        }
    }

    public async Task<Result<AuthorLinkDto>> AddAuthorLinkAsync(int authorId, string? name, string url, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (authorId <= 0)
        {
            return Result.Invalid(new ValidationError(nameof(authorId), "A valid author is required."));
        }

        string? normalizedUrl = NormalizeAuthorLinkUrl(url);
        if (normalizedUrl is null)
        {
            return Result.Invalid(new ValidationError(nameof(url), "Enter a valid http or https URL."));
        }

        string? trimmedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (trimmedName?.Length > 256)
        {
            return Result.Invalid(new ValidationError(nameof(name), "Link label must be 256 characters or fewer."));
        }

        var author = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.Id == authorId,
            CancellationToken = cancellationToken,
        });
        if (author is null)
        {
            return Result.NotFound($"Author {authorId} not found.");
        }

        try
        {
            var link = new AuthorLink
            {
                AuthorId = authorId,
                Name = trimmedName,
                Url = normalizedUrl,
            };
            await authorLinkRepository.InsertAsync(link);

            logger.LogInformation("Added link {LinkId} to author {AuthorId}.", link.Id, authorId);
            return Result.Success(new AuthorLinkDto(link.Id, link.Name, link.Url));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add link to author {AuthorId}", authorId);
            return Result.Error("Could not add the author link.");
        }
    }

    public async Task<Result> RemoveAuthorLinkAsync(int authorId, int linkId, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var link = await authorLinkRepository.FindOneAsync(new SearchOptions<AuthorLink>
        {
            Query = l => l.Id == linkId && l.AuthorId == authorId,
            CancellationToken = cancellationToken,
        });
        if (link is null)
        {
            return Result.NotFound("Author link not found.");
        }

        try
        {
            await authorLinkRepository.DeleteAsync(link);
            logger.LogInformation("Removed link {LinkId} from author {AuthorId}.", linkId, authorId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove link {LinkId} from author {AuthorId}", linkId, authorId);
            return Result.Error("Could not remove the author link.");
        }
    }

    private static string? NormalizeAuthorLinkUrl(string url)
    {
        string trimmed = url.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.ToString()
            : null;
    }

    public async Task<Result<IReadOnlyList<OpenLibraryAuthorMatchDto>>> SearchOpenLibraryAuthorsAsync(string query, int limit = 8, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result.Invalid(new ValidationError(nameof(query), "Search query is required."));
        }

        int clampedLimit = Math.Clamp(limit, 1, 20);
        string trimmed = query.Trim();

        try
        {
            var client = httpClientFactory.CreateClient();
            var rows = await OLSearchLoader.GetAuthorSearchResultsAsync(
                client,
                trimmed,
                new KeyValuePair<string, string>("limit", clampedLimit.ToString()));

            var baseMatches = (rows ?? [])
                .Select(a => new
                {
                    Id = NormalizeOpenLibraryAuthorId(a.ID),
                    a.Name,
                })
                .Where(a => !string.IsNullOrWhiteSpace(a.Id) && !string.IsNullOrWhiteSpace(a.Name))
                .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var matches = new List<OpenLibraryAuthorMatchDto>(baseMatches.Count);
            foreach (var candidate in baseMatches)
            {
                // Search endpoint is intentionally lightweight; hydrate each candidate from
                // /authors/{id}.json so users can choose from real bio/photo-rich records.
                var detail = await OLAuthorLoader.GetDataAsync(client, candidate.Id);
                if (detail is null)
                {
                    continue;
                }

                bool hasBio = !string.IsNullOrWhiteSpace(detail.Bio);
                bool hasPhoto = detail.PhotosIDs.Count > 0;
                if (!hasBio && !hasPhoto)
                {
                    continue;
                }

                string? topBooks = await GetTopBooksAsync(client, candidate.Id);
                int photoId = detail.PhotosIDs.FirstOrDefault();
                matches.Add(new OpenLibraryAuthorMatchDto(
                    NormalizeOpenLibraryAuthorId(detail.ID),
                    string.IsNullOrWhiteSpace(detail.Name) ? candidate.Name : detail.Name.Trim(),
                    string.IsNullOrWhiteSpace(detail.BirthDate) ? null : detail.BirthDate.Trim(),
                    string.IsNullOrWhiteSpace(detail.DeathDate) ? null : detail.DeathDate.Trim(),
                    hasBio,
                    hasPhoto,
                    BuildBioPreview(detail.Bio),
                    photoId > 0 ? $"https://covers.openlibrary.org/a/id/{photoId}-M.jpg" : null,
                    topBooks));
            }

            IReadOnlyList<OpenLibraryAuthorMatchDto> result = matches;
            return Result.Success(result);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenLibrary author search failed for query '{Query}'", trimmed);
            return Result.Error("Failed to search OpenLibrary.");
        }
    }

    public async Task<Result<AuthorOpenLibraryImportResultDto>> ImportFromOpenLibraryAsync(int authorId, string openLibraryAuthorId, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        string olid = NormalizeOpenLibraryAuthorId(openLibraryAuthorId);
        if (string.IsNullOrWhiteSpace(olid))
        {
            return Result.Invalid(new ValidationError(nameof(openLibraryAuthorId), "A valid OpenLibrary author id is required."));
        }

        var author = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.Id == authorId,
            CancellationToken = cancellationToken,
        });
        if (author is null)
        {
            return Result.NotFound($"Author {authorId} not found.");
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            var source = await OLAuthorLoader.GetDataAsync(client, olid);
            if (source is null)
            {
                return Result.NotFound($"OpenLibrary author '{olid}' was not found.");
            }

            string? importedBio = string.IsNullOrWhiteSpace(source.Bio) ? null : source.Bio.Trim();
            bool biographyUpdated = !string.Equals(author.Biography, importedBio, StringComparison.Ordinal);
            if (biographyUpdated)
            {
                author.Biography = importedBio;
                await authorRepository.UpdateAsync(author);
            }

            bool photoUpdated = false;
            int photoId = source.PhotosIDs.FirstOrDefault();
            if (photoId > 0)
            {
                photoUpdated = await TrySaveAuthorPhotoAsync(client, authorId, photoId, cancellationToken);
            }

            return Result.Success(new AuthorOpenLibraryImportResultDto(authorId, olid, biographyUpdated, photoUpdated));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenLibrary import failed for author {AuthorId} using match {OpenLibraryAuthorId}", authorId, openLibraryAuthorId);
            return Result.Error("Failed to import OpenLibrary author metadata.");
        }
    }

    public async Task<Result<AuthorProfileUpdateResultDto>> UpdateProfileAsync(
        int authorId,
        string? biography,
        byte[]? photoBytes,
        string? photoExtension,
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var author = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.Id == authorId,
            CancellationToken = cancellationToken,
        });
        if (author is null)
        {
            return Result.NotFound($"Author {authorId} not found.");
        }

        bool nameUpdated = false;
        if (displayName is not null)
        {
            string trimmedName = displayName.Trim();
            if (string.IsNullOrEmpty(trimmedName))
            {
                return Result.Invalid(new ValidationError(nameof(displayName), "Name cannot be empty."));
            }

            string normalised = trimmedName.ToLowerInvariant();
            if (!string.Equals(author.NormalizedName, normalised, StringComparison.Ordinal))
            {
                var nameTaken = await authorRepository.FindOneAsync(new SearchOptions<Author>
                {
                    Query = a => a.NormalizedName == normalised && a.Id != authorId,
                    CancellationToken = cancellationToken,
                });
                if (nameTaken is not null)
                {
                    return Result.Conflict($"Another author is already named \"{trimmedName}\".");
                }

                author.Name = trimmedName;
                author.NormalizedName = normalised;
                nameUpdated = true;
            }
        }

        string? incomingBio = string.IsNullOrWhiteSpace(biography) ? null : biography.Trim();
        bool biographyUpdated = !string.Equals(author.Biography, incomingBio, StringComparison.Ordinal);
        if (biographyUpdated)
        {
            author.Biography = incomingBio;
        }

        bool photoUpdated = false;
        if (photoBytes is { Length: > 0 })
        {
            photoUpdated = await SaveAuthorPhotoAsync(authorId, photoBytes, photoExtension, cancellationToken);
        }

        if (nameUpdated || biographyUpdated)
        {
            await authorRepository.UpdateAsync(author);
        }

        return Result.Success(new AuthorProfileUpdateResultDto(
            authorId,
            author.Biography,
            biographyUpdated,
            photoUpdated));
    }

    public async Task<Result<int>> DeleteAuthorsWithNoBooksAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var orphans = (await authorRepository.FindAsync(new SearchOptions<Author>
            {
                Query = a => !a.BookAuthors.Any(),
                CancellationToken = cancellationToken,
            })).ToList();

            if (orphans.Count == 0)
            {
                return Result.Success(0);
            }

            foreach (var author in orphans)
            {
                TryDeleteAuthorPhotoFile(author.Id);
            }

            await authorRepository.DeleteAsync(orphans);

            logger.LogInformation("Deleted {Count} author(s) with no linked books.", orphans.Count);
            return Result.Success(orphans.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete authors with no books");
            return Result.Error("Could not delete orphan authors.");
        }
    }

    private void TryDeleteAuthorPhotoFile(int authorId)
    {
        try
        {
            string? path = storagePathProvider.FindAuthorPhotoPath(authorId);
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete on-disk photo for author {AuthorId}", authorId);
        }
    }

    /// <summary>
    /// Deletes the author's subdirectory under <c>_extras</c> if it exists and is empty.
    /// When <paramref name="forceDelete"/> is true the directory is removed even if non-empty
    /// (used when deleting an author outright — files remain in DB with AuthorId null, but their
    /// FilePath still points to the old location so nothing is lost).
    /// </summary>
    private void TryDeleteAuthorExtrasFolder(string authorName, bool forceDelete = false)
    {
        try
        {
            string folder = Path.Combine(storagePathProvider.ExtrasDirectory, SanitizeFolderName(authorName));
            if (!Directory.Exists(folder))
            {
                return;
            }

            if (forceDelete || !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder, recursive: true);
                logger.LogInformation("Deleted empty extras folder '{Folder}'", folder);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove extras folder for author '{Name}'", authorName);
        }
    }

    private static string SanitizeFolderName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(safe) ? "_unknown" : safe;
    }

    private string TryMoveFile(string sourcePath, string targetDir)
    {
        if (!File.Exists(sourcePath))
        {
            return sourcePath;
        }

        string fileName = Path.GetFileName(sourcePath);
        string destPath = Path.Combine(targetDir, fileName);

        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
        {
            return sourcePath;
        }

        if (File.Exists(destPath))
        {
            string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int i = 1;
            do
            {
                destPath = Path.Combine(targetDir, $"{nameNoExt}_{i}{ext}");
                i++;
            }
            while (File.Exists(destPath));
        }

        try
        {
            File.Move(sourcePath, destPath);
            return destPath;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not move extra content file '{Src}' → '{Dst}'", sourcePath, destPath);
            return sourcePath;
        }
    }

    private async Task<bool> TrySaveAuthorPhotoAsync(HttpClient client, int authorId, int photoId, CancellationToken cancellationToken)
    {
        var (ok, bytes) = await OLImageLoader.TryGetAuthorPhotoAsync(
            client,
            AuthorPhotoIdType.ID,
            photoId.ToString(),
            ImageSize.Medium);

        if (!ok || bytes is null || bytes.Length == 0)
        {
            return false;
        }

        try
        {
            return await SaveAuthorPhotoAsync(authorId, bytes, "jpg", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist OpenLibrary photo for author {AuthorId}", authorId);
            return false;
        }
    }

    private async Task<bool> SaveAuthorPhotoAsync(int authorId, byte[] bytes, string? extension, CancellationToken cancellationToken)
    {
        string normalizedExt = NormalizeImageExtension(extension);

        string? existing = storagePathProvider.FindAuthorPhotoPath(authorId);
        if (!string.IsNullOrWhiteSpace(existing) && File.Exists(existing))
        {
            File.Delete(existing);
        }

        string path = Path.Combine(storagePathProvider.AuthorPhotosDirectory, $"{authorId}.{normalizedExt}");
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return true;
    }

    private static string NormalizeOpenLibraryAuthorId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string value = raw.Trim();
        if (value.StartsWith("/authors/", StringComparison.OrdinalIgnoreCase))
        {
            value = value["/authors/".Length..];
        }

        return value;
    }

    private static string? BuildBioPreview(string? bio)
    {
        if (string.IsNullOrWhiteSpace(bio))
        {
            return null;
        }

        string trimmed = bio.Trim();
        const int max = 180;
        return trimmed.Length <= max ? trimmed : $"{trimmed[..max]}...";
    }

    private async Task<string?> GetTopBooksAsync(HttpClient client, string olid, int count = 3)
    {
        try
        {
            // Prefer English editions; fall back to all languages if too few English results.
            var titles = await FetchTopBookTitlesAsync(client, olid, count, englishOnly: true);
            if (titles.Count < count)
            {
                titles = await FetchTopBookTitlesAsync(client, olid, count, englishOnly: false);
            }

            return titles.Count > 0 ? string.Join(", ", titles) : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch works for OpenLibrary author '{OLId}'", olid);
            return null;
        }
    }

    /// <summary>
    /// Fetches the most widely-published works for an author from the Open Library search API,
    /// sorted by edition count as a proxy for recognition. Results are optionally restricted to
    /// works that have at least one English edition.
    /// </summary>
    private async Task<List<string>> FetchTopBookTitlesAsync(
        HttpClient client, string olid, int count, bool englishOnly)
    {
        // Fetch a few extra so minor gaps (blank titles, etc.) don't reduce the final count.
        int fetchLimit = count + 3;
        string langSegment = englishOnly ? "&language=eng" : string.Empty;
        // author_key expects the bare OLID (e.g. OL26320A), not the full /authors/OL26320A key.
        string url = $"https://openlibrary.org/search.json?author_key={olid}&sort=editions&limit={fetchLimit}&fields=title{langSegment}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", MetadataHttp.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        if (!doc.RootElement.TryGetProperty("docs", out var docs))
        {
            return [];
        }

        return docs.EnumerateArray()
            .Select(d => d.TryGetProperty("title", out var t) ? t.GetString()?.Trim() : null)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Take(count)
            .ToList()!;
    }

    private static string NormalizeImageExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "jpg";
        }

        string ext = extension.Trim().TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "jpg" or "jpeg" => "jpg",
            "png" => "png",
            "gif" => "gif",
            "webp" => "webp",
            "bmp" => "bmp",
            _ => "jpg",
        };
    }
}