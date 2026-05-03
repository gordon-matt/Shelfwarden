using OpenLibraryNET.Loader;
using OpenLibraryNET.Utility;
using Shelfwarden.Services.Storage;

namespace Shelfwarden.Services;

public class AuthorService(
    ILogger<AuthorService> logger,
    IRepository<Author> authorRepository,
    IRepository<Book> bookRepository,
    IRepository<BookAuthor> bookAuthorRepository,
    IRepository<BookProgress> progressRepository,
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
            OrderBy = q => q.OrderBy(a => a.NormalizedName),
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

    public async Task<Result<IReadOnlyList<AuthorListItemDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        var options = new SearchOptions<Author>
        {
            OrderBy = q => q.OrderBy(a => a.NormalizedName),
            CancellationToken = cancellationToken,
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            string needle = query.Trim().ToLowerInvariant();
            options.Query = a => EF.Functions.Like(a.NormalizedName, $"%{needle}%");
        }

        var authors = (await authorRepository.FindAsync(options)).ToList();
        var ids = authors.Select(a => a.Id).ToList();

        // One join-table query → counts by author id. Cheaper than N round-trips.
        var counts = (await bookAuthorRepository.FindAsync(
                new SearchOptions<BookAuthor> { Query = ba => ids.Contains(ba.AuthorId) },
                ba => ba.AuthorId))
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
            CancellationToken = cancellationToken,
        });
        if (author is null)
        {
            return Result.NotFound($"Author {id} not found.");
        }

        // Load every book for the author with everything we need to build BookListItemDto
        // entries. The author can have at most a few hundred books in any realistic scenario,
        // so a single Include is fine.
        var books = (await bookRepository.FindAsync(new SearchOptions<Book>
        {
            Query = b => b.BookAuthors.Any(ba => ba.AuthorId == id),
            Include = q => q
                .Include(b => b.Series)
                .Include(b => b.BookAuthors).ThenInclude(ba => ba.Author),
            OrderBy = q => q
                .OrderBy(b => b.SeriesId == null ? 1 : 0)
                .ThenBy(b => b.NumberInSeries)
                .ThenBy(b => b.SortTitle ?? b.Title),
            SplitQuery = true,
            CancellationToken = cancellationToken,
        })).ToList();

        // Per-user progress so the in-page cards can render the green progress bar.
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
                        b.CoverImagePath,
                        BookCoverCaching.GetCoverCacheVersion(b.LastScannedAt, b.UpdatedAt, b.CreatedAt)))
                    .ToList()))
            .OrderBy(s => s.SeriesName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var standalone = books
            .Where(b => !b.SeriesId.HasValue)
            .Select(b => BookProjections.ToListItem(b, progressByBook.GetValueOrDefault(b.Id)))
            .ToList();

        return Result.Success(new AuthorDetailDto(
            author.Id,
            author.Name,
            author.Biography,
            books.Count,
            seriesGroups,
            standalone));
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
                    Name = a.Name,
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
                    matches.Add(new OpenLibraryAuthorMatchDto(
                        candidate.Id,
                        candidate.Name,
                        null,
                        null,
                        HasBio: false,
                        HasPhoto: false,
                        BioPreview: null));
                    continue;
                }

                matches.Add(new OpenLibraryAuthorMatchDto(
                    NormalizeOpenLibraryAuthorId(detail.ID),
                    string.IsNullOrWhiteSpace(detail.Name) ? candidate.Name : detail.Name.Trim(),
                    string.IsNullOrWhiteSpace(detail.BirthDate) ? null : detail.BirthDate.Trim(),
                    string.IsNullOrWhiteSpace(detail.DeathDate) ? null : detail.DeathDate.Trim(),
                    !string.IsNullOrWhiteSpace(detail.Bio),
                    detail.PhotosIDs.Count > 0,
                    BuildBioPreview(detail.Bio)));
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