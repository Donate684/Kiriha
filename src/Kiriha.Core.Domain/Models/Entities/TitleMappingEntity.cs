namespace Kiriha.Core.Domain.Models.Entities;

/// <summary>
/// User-curated mapping from a normalized file/window release title to a tracker ID
/// (or <c>-1</c> when explicitly unlinked / negatively mapped).
/// </summary>
public class TitleMappingEntity
{
    public string NormalizedTitle { get; set; } = string.Empty;
    public int TargetId { get; set; }
    public MediaKind MediaKind { get; set; } = MediaKind.Anime;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
