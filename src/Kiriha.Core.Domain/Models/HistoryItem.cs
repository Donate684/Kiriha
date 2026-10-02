using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Kiriha.Core.Domain.Models;

public class HistoryItem
{
    public int Id { get; set; }
    public int AnimeId { get; set; }
    public string AnimeTitle { get; set; } = string.Empty;
    public string? RussianTitle { get; set; }
    public int Episode { get; set; }
    public DateTime Timestamp { get; set; }

    // Optional: for differentiation
    public int ActionType { get; set; } = 1; // 1: Watched, etc.
    public string Detail { get; set; } = string.Empty;

    public string? TrackerStatusJson { get; set; }

    /// <summary>Runtime-resolved poster URL (from user's AnimeRepository.Collection). Not persisted.</summary>
    [NotMapped]
    public string? PosterUrl { get; set; }

    [NotMapped]
    private Dictionary<string, TrackerSyncInfo>? _trackerStatuses;

    [NotMapped]
    public Dictionary<string, TrackerSyncInfo> TrackerStatuses
    {
        get
        {
            if (_trackerStatuses != null) return _trackerStatuses;
            if (string.IsNullOrWhiteSpace(TrackerStatusJson))
            {
                _trackerStatuses = new Dictionary<string, TrackerSyncInfo>(StringComparer.OrdinalIgnoreCase);
                return _trackerStatuses;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, TrackerSyncInfo>>(TrackerStatusJson);
                _trackerStatuses = parsed != null
                    ? new Dictionary<string, TrackerSyncInfo>(parsed, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, TrackerSyncInfo>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                _trackerStatuses = new Dictionary<string, TrackerSyncInfo>(StringComparer.OrdinalIgnoreCase);
            }
            return _trackerStatuses;
        }
        set
        {
            _trackerStatuses = value;
            TrackerStatusJson = JsonSerializer.Serialize(value);
        }
    }

    public void SetTrackerStatus(string trackerName, TrackerSyncState state, string? error = null)
    {
        var dict = TrackerStatuses;
        if (!dict.TryGetValue(trackerName, out var info))
        {
            info = new TrackerSyncInfo();
            dict[trackerName] = info;
        }
        info.State = state;
        info.ErrorMessage = error;
        info.UpdatedAt = DateTime.UtcNow;
        TrackerStatusJson = JsonSerializer.Serialize(dict);
    }
}

