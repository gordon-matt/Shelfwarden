namespace Shelfwarden.Models;

/// <summary>A genre. Names are matched without case.</summary>
/// <param name="Id">Genre id.</param>
/// <param name="Name">Display name.</param>
public record GenreDto(int Id, string Name);