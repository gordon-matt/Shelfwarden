using Hangfire;
using Shelfwarden.Services.Jobs;

namespace Shelfwarden.Infrastructure;

/// <summary>Registers <see cref="RecurringJob"/> definitions (shared by web and desktop hosts).</summary>
public static class HangfireRecurringJobs
{
    public static void Register() =>
        RecurringJob.AddOrUpdate<AuthorCleanupJob>(
            "cleanup-authors-without-books",
            job => job.RunAsync(CancellationToken.None),
            Cron.Weekly(DayOfWeek.Monday, 4),
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}