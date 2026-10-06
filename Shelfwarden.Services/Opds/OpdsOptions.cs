namespace Shelfwarden.Services.Opds;

/// <summary>Bound from the <c>Opds</c> configuration section.</summary>
public class OpdsOptions
{
    public const string SectionName = "Opds";

    public const int MaxPageSize = 500;

    /// <summary>Entries per page on paginated feeds. Clamped to 1..<see cref="MaxPageSize"/>.</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>Failed sign-ins allowed per client address and user name within <see cref="FailedAuthWindow"/>.</summary>
    public int MaxFailedAuthAttempts { get; set; } = 10;

    /// <summary>How long failed sign-ins are remembered, and how long a client is blocked once it hits the limit.</summary>
    public TimeSpan FailedAuthWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Longest edge, in pixels, of generated cover thumbnails.</summary>
    public int ThumbnailSize { get; set; } = 300;

    public int EffectivePageSize => Math.Clamp(PageSize, 1, MaxPageSize);
}
