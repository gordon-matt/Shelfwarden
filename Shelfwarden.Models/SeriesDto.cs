namespace Shelfwarden.Models;

public record SeriesDto(
    int Id,
    string Name,
    string? Description,
    int BookCount,
    int? UniverseId = null,
    string? UniverseName = null);