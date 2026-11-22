namespace Shelfwarden.Models;

/// <summary>
/// A series and how many books it holds. <paramref name="UniverseId"/> is <see langword="null"/> when the series is not in a universe.
/// </summary>
/// <param name="Id">Series id.</param>
/// <param name="Name">Series name.</param>
/// <param name="Description">Description, or <see langword="null"/>.</param>
/// <param name="BookCount">Books in the series.</param>
/// <param name="UniverseId">Universe the series belongs to, or <see langword="null"/>.</param>
/// <param name="UniverseName">That universe's name, or <see langword="null"/> when <paramref name="UniverseId"/> is.</param>
public record SeriesDto(
    int Id,
    string Name,
    string? Description,
    int BookCount,
    int? UniverseId = null,
    string? UniverseName = null);