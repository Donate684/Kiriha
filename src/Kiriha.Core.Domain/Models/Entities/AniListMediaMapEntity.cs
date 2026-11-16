namespace Kiriha.Core.Domain.Models.Entities;

/// <summary>
/// Persisted mapping for AniList entries that do not have a corresponding MyAnimeList ID (<c>idMal == null</c>).
/// </summary>
public class AniListMediaMapEntity
{
    public int PrimaryId { get; set; }
    public string MediaType { get; set; } = "ANIME";
    public int AniListId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
