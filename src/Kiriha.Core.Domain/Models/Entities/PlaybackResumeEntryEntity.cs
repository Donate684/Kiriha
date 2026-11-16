namespace Kiriha.Core.Domain.Models.Entities;

/// <summary>
/// Persisted resume position for a local file or stream URL played in the built-in player.
/// </summary>
public class PlaybackResumeEntryEntity
{
    public string VideoUrl { get; set; } = string.Empty;
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
