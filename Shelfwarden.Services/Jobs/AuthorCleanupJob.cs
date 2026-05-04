namespace Shelfwarden.Services.Jobs;

/// <summary>
/// Hangfire entrypoint: removes <see cref="Data.Entities.Author"/> rows that no longer have
/// any <see cref="Data.Entities.BookAuthor"/> links.
/// </summary>
public class AuthorCleanupJob(
    IAuthorService authorService,
    ILogger<AuthorCleanupJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var result = await authorService.DeleteAuthorsWithNoBooksAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            logger.LogWarning(
                "Author orphan cleanup failed: {Error}",
                result.Errors.FirstOrDefault() ?? "Unknown error");
            return;
        }

        if (result.Value > 0)
        {
            logger.LogInformation("Author orphan cleanup finished; removed {Count} author(s).", result.Value);
        }
    }
}
