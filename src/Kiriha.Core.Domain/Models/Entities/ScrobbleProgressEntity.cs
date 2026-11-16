namespace Kiriha.Core.Domain.Models.Entities;

/// <summary>
/// Persisted cumulative watch time (in seconds) toward the scrobble threshold for an active episode key.
/// </summary>
public class ScrobbleProgressEntity
{
    public string Key { get; set; } = string.Empty;
    public int ElapsedSeconds { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
