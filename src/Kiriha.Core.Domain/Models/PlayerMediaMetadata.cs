namespace Kiriha.Core.Domain.Models;

public sealed record PlayerMediaMetadata(
    string OriginalTitle,
    string TitleRu,
    string TitleEn,
    string EpisodeText,
    int? AnimeId,
    string TitleRomaji = "",
    string TorrentHash = "")
{
    public PlayerMediaMetadata() : this(string.Empty, string.Empty, string.Empty, string.Empty, null, string.Empty, string.Empty) { }

    public string? PlaybackRequestId { get; init; }
    public string? PlaybackSourceKey { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasPlaybackIdentity => !string.IsNullOrWhiteSpace(PlaybackRequestId) || !string.IsNullOrWhiteSpace(PlaybackSourceKey);

    public bool MatchesPlayback(string? requestId, string? sourceKey, string originalTitle, string episode)
    {
        if (!string.IsNullOrWhiteSpace(PlaybackRequestId))
            return string.Equals(PlaybackRequestId, requestId, StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(PlaybackSourceKey) || string.Equals(PlaybackSourceKey, sourceKey, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(PlaybackSourceKey))
            return string.IsNullOrWhiteSpace(requestId) && string.Equals(PlaybackSourceKey, sourceKey, StringComparison.Ordinal);

        if (string.IsNullOrWhiteSpace(OriginalTitle) || string.IsNullOrWhiteSpace(originalTitle) ||
            IsStreamManifest(OriginalTitle) || IsStreamManifest(originalTitle))
            return false;

        var left = OriginalTitle.Trim();
        var right = originalTitle.Trim();
        bool sameTitle = string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ||
            ((!Path.HasExtension(left) || !Path.HasExtension(right)) &&
                string.Equals(Path.GetFileNameWithoutExtension(left), Path.GetFileNameWithoutExtension(right), StringComparison.OrdinalIgnoreCase));
        return sameTitle && (string.IsNullOrWhiteSpace(EpisodeText) || string.IsNullOrWhiteSpace(episode) ||
            string.Equals(EpisodeText.Trim(), episode.Trim(), StringComparison.OrdinalIgnoreCase) ||
            (int.TryParse(EpisodeText, out var leftEpisode) && int.TryParse(episode, out var rightEpisode) && leftEpisode == rightEpisode));
    }

    public static bool IsStreamManifest(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase))
            return true;

        var cleanName = System.IO.Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(cleanName))
            cleanName = name;

        if (cleanName.Contains(":hls", StringComparison.OrdinalIgnoreCase) ||
            cleanName.Contains(":manifest", StringComparison.OrdinalIgnoreCase))
            return true;

        if (cleanName.Equals("manifest", StringComparison.OrdinalIgnoreCase) ||
            cleanName.Equals("master", StringComparison.OrdinalIgnoreCase) ||
            cleanName.Equals("index", StringComparison.OrdinalIgnoreCase) ||
            cleanName.Equals("playlist", StringComparison.OrdinalIgnoreCase) ||
            cleanName.Equals("video", StringComparison.OrdinalIgnoreCase) ||
            cleanName.Equals("stream", StringComparison.OrdinalIgnoreCase) ||
            cleanName.Equals("media", StringComparison.OrdinalIgnoreCase) ||
            cleanName.Equals("chunk", StringComparison.OrdinalIgnoreCase) ||
            cleanName.Equals("hls", StringComparison.OrdinalIgnoreCase))
            return true;

        var span = cleanName.AsSpan().Trim();
        if (span.EndsWith("p", StringComparison.OrdinalIgnoreCase))
            span = span[..^1];

        if (!span.IsEmpty && IsAllDigits(span))
            return true;

        if (cleanName.Length >= 16 && IsHexHash(cleanName))
            return true;

        return false;
    }

    private static bool IsAllDigits(ReadOnlySpan<char> span)
    {
        foreach (var c in span)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }
        return true;
    }

    private static bool IsHexHash(string str)
    {
        foreach (var c in str)
        {
            if (!char.IsAsciiHexDigit(c))
                return false;
        }
        return true;
    }

    public static bool IsStreamUrl(string? videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            return false;

        if (Uri.TryCreate(videoPath, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var query = uri.Query;
            bool isTorrServer = !string.IsNullOrEmpty(query) &&
                System.Text.RegularExpressions.Regex.IsMatch(query, Constants.AppConstants.Torrents.TorrentHashLinkRegex, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(500));

            var seg = uri.Segments.LastOrDefault() ?? string.Empty;
            var rawName = System.IO.Path.GetFileNameWithoutExtension(seg);
            var filename = Uri.UnescapeDataString(rawName);

            if (isTorrServer)
            {
                if (!string.IsNullOrWhiteSpace(filename) && !IsStreamManifest(filename) && !filename.Equals("stream", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IsStreamManifest(filename))
            {
                return true;
            }

            return false;
        }

        return false;
    }

    public static PlayerMediaMetadata FromVideoPath(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            return new PlayerMediaMetadata();

        string fallbackTitle;
        string torrentHash = string.Empty;

        if (Uri.TryCreate(videoPath, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var seg = uri.Segments.LastOrDefault() ?? string.Empty;
            var rawName = System.IO.Path.GetFileNameWithoutExtension(seg);
            fallbackTitle = Uri.UnescapeDataString(rawName);

            var query = uri.Query;
            if (!string.IsNullOrEmpty(query))
            {
                var match = System.Text.RegularExpressions.Regex.Match(query, Constants.AppConstants.Torrents.TorrentHashLinkRegex, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(500));
                if (match.Success)
                {
                    torrentHash = match.Groups[1].Value;
                }
            }
        }
        else
        {
            fallbackTitle = System.IO.Path.GetFileNameWithoutExtension(videoPath);
        }

        if (IsStreamUrl(videoPath) || IsStreamManifest(fallbackTitle))
        {
            fallbackTitle = string.Empty;
        }

        return new PlayerMediaMetadata(fallbackTitle, fallbackTitle, string.Empty, string.Empty, null, fallbackTitle, torrentHash);
    }
}
