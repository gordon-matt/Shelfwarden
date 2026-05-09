namespace Shelfwarden.Services;

public interface IShelfService
{
    Task<Result<IReadOnlyList<ShelfDto>>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Result<ShelfDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<ShelfDto>> CreateAsync(CreateShelfRequest request, CancellationToken cancellationToken = default);

    Task<Result<ShelfDto>> UpdateAsync(int id, UpdateShelfRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<Result> ScheduleScanAsync(int id, CancellationToken cancellationToken = default);

    Task<Result> UploadCardBannerAsync(
        int shelfId,
        Stream content,
        string fileName,
        long? contentLength,
        CancellationToken cancellationToken = default);
}