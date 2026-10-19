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

    public static bool IsStreamManifest(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.Contains(":hls", StringComparison.OrdinalIgnoreCase) ||
               name.Contains(":manifest", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("manifest", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("master", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("index", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("playlist", StringComparison.OrdinalIgnoreCase);
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
                var match = System.Text.RegularExpressions.Regex.Match(query, Constants.AppConstants.Torrents.TorrentHashLinkRegex);
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

        if (IsStreamManifest(fallbackTitle))
        {
            fallbackTitle = string.Empty;
        }

        return new PlayerMediaMetadata(fallbackTitle, fallbackTitle, string.Empty, string.Empty, null, fallbackTitle, torrentHash);
    }
}
