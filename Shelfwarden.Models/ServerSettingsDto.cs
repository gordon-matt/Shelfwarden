namespace Shelfwarden.Models;

/// <summary>
/// Strongly-typed view over the <c>ServerSettings</c> key/value table. The settings service
/// loads every row once on read and projects into this record so consumers don't have to know
/// the magic key strings.
/// </summary>
public record ServerSettingsDto
{
    /// <summary>Active Bootswatch theme name (e.g. <c>"flatly"</c>, <c>"darkly"</c>).</summary>
    public required string Theme { get; init; }

    /// <summary>True once the first-run wizard has completed.</summary>
    public bool SetupComplete { get; init; }
}

public record UpdateThemeRequest
{
    [Required, StringLength(64)]
    public required string Theme { get; init; }
}
