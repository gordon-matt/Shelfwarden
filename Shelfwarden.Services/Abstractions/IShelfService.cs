namespace Shelfwarden.Services;

/// <summary>
/// Library roots. Each shelf owns one or more folders and the books scanned from them.
/// Administrators see every shelf. Other users see unrestricted shelves and shelves they
/// are allowed to open. A shelf they cannot open is reported as missing.
/// </summary>
public interface IShelfService
{
    /// <summary>Shelves the caller can open, by name.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The shelves, ordered by name.</returns>
    Task<Result<IReadOnlyList<ShelfDto>>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>One shelf the caller can open, including its folders and who may see it.</summary>
    /// <param name="id">Shelf id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The shelf, or not found when the caller cannot open it.</returns>
    Task<Result<ShelfDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a shelf. Administrator only. At least one folder is required, the name must be
    /// unique, and a folder already used by another shelf is refused. An empty user and role
    /// list leaves the shelf open to every signed-in user.
    /// </summary>
    /// <param name="request">Name, folders, access lists, and import options.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The new shelf, or forbidden, invalid, or conflict.</returns>
    Task<Result<ShelfDto>> CreateAsync(CreateShelfRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the name, description, folders, import options, access lists, and card banner.
    /// Administrator only. <see cref="Shelf.DirectoryStructure"/> stays as it was set at creation.
    /// Dropping a folder does not delete books until the next scan.
    /// </summary>
    /// <param name="id">Shelf id.</param>
    /// <param name="request">The replacement. Folder layout is not on this request.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The updated shelf, or forbidden, not found, invalid, or conflict.</returns>
    Task<Result<ShelfDto>> UpdateAsync(int id, UpdateShelfRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the shelf, its folders, its access rows, and its book rows. Administrator only.
    /// The ebook files are left on disk. The card banner file is removed.
    /// </summary>
    /// <param name="id">Shelf id.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>Success, or forbidden or not found.</returns>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a scan of one shelf. Administrator only. A scan that is already running keeps the
    /// new one waiting; see <see cref="Scanning.IScannerService.ScanShelfAsync"/>.
    /// </summary>
    /// <param name="id">Shelf id.</param>
    /// <param name="cancellationToken">Cancels the lookup. Queuing the job is not cancelled once it has started.</param>
    /// <returns>Success, or forbidden or not found.</returns>
    Task<Result> ScheduleScanAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an uploaded banner and switches the shelf to that image. Administrator only.
    /// Accepts JPG, PNG, GIF, or WebP, up to 2 MB.
    /// </summary>
    /// <param name="shelfId">Shelf id.</param>
    /// <param name="content">The image.</param>
    /// <param name="fileName">Original file name. The extension must be JPG, PNG, GIF, or WebP.</param>
    /// <param name="contentLength">Length in bytes, when the caller knows it. Over 2 MB is rejected.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Success, or forbidden, not found, or invalid.</returns>
    Task<Result> UploadCardBannerAsync(
        int shelfId,
        Stream content,
        string fileName,
        long? contentLength,
        CancellationToken cancellationToken = default);
}