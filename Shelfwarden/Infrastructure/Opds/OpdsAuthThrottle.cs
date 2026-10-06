using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shelfwarden.Services.Opds;

namespace Shelfwarden.Infrastructure.Opds;

/// <summary>
/// Counts failed OPDS sign-ins per client key (remote address + attempted user name) and blocks the
/// key once <see cref="OpdsOptions.MaxFailedAuthAttempts"/> is reached, until
/// <see cref="OpdsOptions.FailedAuthWindow"/> has passed since the first failure.
/// In-memory, so limits are per server process.
/// </summary>
public sealed class OpdsAuthThrottle(IMemoryCache cache, IOptions<OpdsOptions> options, TimeProvider timeProvider)
{
    private sealed class Counter
    {
        public int Failures;
        public DateTimeOffset ExpiresAt;
    }

    /// <summary>When the key is blocked, how long until it may try again.</summary>
    public TimeSpan? GetBlockedFor(string clientKey)
    {
        if (!cache.TryGetValue(CacheKey(clientKey), out Counter? counter) || counter is null)
        {
            return null;
        }

        if (Volatile.Read(ref counter.Failures) < Math.Max(1, options.Value.MaxFailedAuthAttempts))
        {
            return null;
        }

        var remaining = counter.ExpiresAt - timeProvider.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    public void RecordFailure(string clientKey)
    {
        var counter = cache.GetOrCreate(CacheKey(clientKey), entry =>
        {
            var window = options.Value.FailedAuthWindow > TimeSpan.Zero ? options.Value.FailedAuthWindow : TimeSpan.FromMinutes(15);
            entry.AbsoluteExpirationRelativeToNow = window;
            return new Counter { ExpiresAt = timeProvider.GetUtcNow() + window };
        })!;

        Interlocked.Increment(ref counter.Failures);
    }

    public void Reset(string clientKey) => cache.Remove(CacheKey(clientKey));

    private static string CacheKey(string clientKey) => $"opds-auth-failures:{clientKey}";
}
