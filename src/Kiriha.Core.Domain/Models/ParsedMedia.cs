using System.Security.Cryptography;
using System.Text;

namespace Kiriha.Core.Domain.Models;

public record class ParsedMedia
{
    public string OriginalTitle { get; set; } = string.Empty;
    public string AnimeTitle { get; set; } = string.Empty;
    public string EpisodeTitle { get; set; } = string.Empty;
    public string Episode { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    /// <summary>
    /// OS process id of the player. 0 when unknown. Populated by
    /// <c>DetectionManager</c> so downstream consumers (audio-session probe,
    /// scrobbler) can identify the source process without re-scanning.
    /// </summary>
    public uint Pid { get; set; }
    public bool IsPlaying { get; set; } = true;

    // Additional properties expected by UI/ViewModels
    public string VideoResolution { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string AnimeType { get; set; } = string.Empty;
    public TimeSpan? Position { get; set; }
    public TimeSpan? Duration { get; set; }
    public string TorrentHash { get; set; } = string.Empty;
    public string? PlaybackRequestId { get; set; }
    public string? PlaybackSourceKey { get; set; }

    public ParsedMedia WithEpisode(string newEpisode) => this with { Episode = newEpisode };

    public bool HasSamePlaybackSource(ParsedMedia other) =>
        string.Equals(ProcessName, other.ProcessName, StringComparison.OrdinalIgnoreCase) &&
        (Pid == other.Pid || Pid == 0 || other.Pid == 0) &&
        string.Equals(PlaybackRequestId, other.PlaybackRequestId, StringComparison.Ordinal) &&
        (string.IsNullOrEmpty(TorrentHash) || string.IsNullOrEmpty(other.TorrentHash) ||
            string.Equals(TorrentHash, other.TorrentHash, StringComparison.OrdinalIgnoreCase));

    public bool HasSameSourceFile(ParsedMedia other)
    {
        if (!string.IsNullOrEmpty(PlaybackSourceKey) && !string.IsNullOrEmpty(other.PlaybackSourceKey))
            return string.Equals(PlaybackSourceKey, other.PlaybackSourceKey, StringComparison.Ordinal);

        return string.Equals(OriginalTitle, other.OriginalTitle, StringComparison.OrdinalIgnoreCase) ||
            ((!Path.HasExtension(OriginalTitle) || !Path.HasExtension(other.OriginalTitle)) &&
                string.Equals(Path.GetFileNameWithoutExtension(OriginalTitle), Path.GetFileNameWithoutExtension(other.OriginalTitle),
                    StringComparison.OrdinalIgnoreCase));
    }

    public static string CreatePlaybackSourceKey(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
}
