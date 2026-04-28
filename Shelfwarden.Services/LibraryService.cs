using Hangfire;
using Shelfwarden.Services.Scanning;

namespace Shelfwarden.Services;

public class LibraryService(
    ILogger<LibraryService> logger,
    IUserContextService userContext,
    IBackgroundJobClient backgroundJobs,
    IRepository<Library> libraryRepository,
    IRepository<LibraryFolder> folderRepository,
    IRepository<Book> bookRepository) : ILibraryService
{
    public async Task<Result<IReadOnlyList<LibraryDto>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var libraries = await libraryRepository.FindAsync(new SearchOptions<Library>
        {
            Include = q => q.Include(x => x.Folders),
            OrderBy = q => q.OrderBy(x => x.Name),
        });

        var bookCounts = await bookRepository.FindAsync(
            new SearchOptions<Book>(),
            b => new { b.LibraryId });

        var counts = bookCounts
            .GroupBy(x => x.LibraryId)
            .ToDictionary(g => g.Key, g => g.Count());

        IReadOnlyList<LibraryDto> result = libraries
            .Select(l => MapLibrary(l, counts.GetValueOrDefault(l.Id, 0)))
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<LibraryDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var library = await libraryRepository.FindOneAsync(new SearchOptions<Library>
        {
            Query = x => x.Id == id,
            Include = q => q.Include(x => x.Folders),
        });

        if (library is null)
        {
            return Result.NotFound($"Library {id} not found.");
        }

        int bookCount = (await bookRepository.FindAsync(
                new SearchOptions<Book> { Query = b => b.LibraryId == id },
                b => b.Id))
            .Count();

        return Result.Success(MapLibrary(library, bookCount));
    }

    public async Task<Result<LibraryDto>> CreateAsync(CreateLibraryRequest request, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var folderPaths = NormalizeFolders(request.Folders);
        if (folderPaths.Count == 0)
        {
            return Result.Invalid(new ValidationError(nameof(request.Folders), "At least one folder is required."));
        }

        var existing = await libraryRepository.FindOneAsync(new SearchOptions<Library>
        {
            Query = l => l.Name == request.Name,
        });
        if (existing is not null)
        {
            return Result.Conflict($"A library named '{request.Name}' already exists.");
        }

        var library = await libraryRepository.InsertAsync(new Library
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
        });

        var folders = folderPaths
            .Select(p => new LibraryFolder { LibraryId = library.Id, Path = p })
            .ToList();
        await folderRepository.InsertAsync(folders);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Created library {LibraryId} '{Name}' with {FolderCount} folder(s)",
                library.Id, library.Name, folders.Count);
        }

        return await GetByIdAsync(library.Id, cancellationToken);
    }

    public async Task<Result<LibraryDto>> UpdateAsync(int id, UpdateLibraryRequest request, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var library = await libraryRepository.FindOneAsync(new SearchOptions<Library>
        {
            Query = x => x.Id == id,
            Include = q => q.Include(x => x.Folders),
        });
        if (library is null)
        {
            return Result.NotFound($"Library {id} not found.");
        }

        var nameClash = await libraryRepository.FindOneAsync(new SearchOptions<Library>
        {
            Query = l => l.Id != id && l.Name == request.Name,
        });
        if (nameClash is not null)
        {
            return Result.Conflict($"Another library is already named '{request.Name}'.");
        }

        library.Name = request.Name.Trim();
        library.Description = request.Description?.Trim();
        await libraryRepository.UpdateAsync(library);

        var desiredFolders = NormalizeFolders(request.Folders);
        var existingFolders = library.Folders.ToList();

        var toRemove = existingFolders
            .Where(f => !desiredFolders.Contains(f.Path, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (toRemove.Count > 0)
        {
            await folderRepository.DeleteAsync(toRemove);
        }

        var existingPaths = existingFolders.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toAdd = desiredFolders
            .Where(p => !existingPaths.Contains(p))
            .Select(p => new LibraryFolder { LibraryId = id, Path = p })
            .ToList();
        if (toAdd.Count > 0)
        {
            await folderRepository.InsertAsync(toAdd);
        }

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var library = await libraryRepository.FindOneAsync(new SearchOptions<Library>
        {
            Query = x => x.Id == id,
        });
        if (library is null)
        {
            return Result.NotFound();
        }

        // FK ON DELETE CASCADE handles folders + books.
        await libraryRepository.DeleteAsync(library);
        return Result.Success();
    }

    public async Task<Result> ScheduleScanAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var library = await libraryRepository.FindOneAsync(new SearchOptions<Library>
        {
            Query = l => l.Id == id,
        });
        if (library is null)
        {
            return Result.NotFound();
        }

        // Hangfire's [DisableConcurrentExecution] on IScannerService.ScanLibraryAsync stops the
        // same library being scanned twice concurrently — multiple enqueues just queue up.
        string jobId = backgroundJobs.Enqueue<IScannerService>(s => s.ScanLibraryAsync(id, CancellationToken.None));

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Enqueued scan for library {LibraryId} as Hangfire job {JobId}", id, jobId);
        }

        return Result.Success();
    }

    private static List<string> NormalizeFolders(IReadOnlyList<string> folders)
        => [.. folders
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().TrimEnd('/', '\\'))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static LibraryDto MapLibrary(Library library, int bookCount)
        => new(
            library.Id,
            library.Name,
            library.Description,
            library.LastScannedAt,
            library.CreatedAt,
            bookCount,
            library.Folders
                .OrderBy(f => f.Path)
                .Select(f => new LibraryFolderDto(f.Id, f.Path))
                .ToList());
}