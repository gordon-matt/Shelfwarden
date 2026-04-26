namespace Shelfwarden.Services;

public interface ILibraryService
{
    Task<Result<IReadOnlyList<LibraryDto>>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Result<LibraryDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<LibraryDto>> CreateAsync(CreateLibraryRequest request, CancellationToken cancellationToken = default);

    Task<Result<LibraryDto>> UpdateAsync(int id, UpdateLibraryRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Enqueue a Hangfire scan job for the given library. No-op if already running.</summary>
    Task<Result> ScheduleScanAsync(int id, CancellationToken cancellationToken = default);
}
