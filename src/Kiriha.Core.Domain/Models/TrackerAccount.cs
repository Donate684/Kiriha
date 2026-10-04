using System.Text.Json.Serialization;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Api;

namespace Kiriha.Core.Domain.Models;

public class TrackerAccount
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("tracker_id")]
    public string TrackerId { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("is_primary")]
    public bool IsPrimary { get; set; }

    [JsonPropertyName("is_mirror")]
    public bool IsMirror { get; set; } = true;

    [JsonPropertyName("tokens")]
    public OAuthTokens? Tokens { get; set; }

    [JsonIgnore]
    public string DisplayName => !string.IsNullOrWhiteSpace(Username)
        ? $"{TrackerConstants.GetDefaultDisplayName(TrackerId)} ({Username})"
        : TrackerConstants.GetDefaultDisplayName(TrackerId);

    public TrackerAccount Clone() => new()
    {
        Id = Id,
        TrackerId = TrackerId,
        Username = Username,
        IsEnabled = IsEnabled,
        IsPrimary = IsPrimary,
        IsMirror = IsMirror,
        Tokens = Tokens?.Clone()
    };
}
