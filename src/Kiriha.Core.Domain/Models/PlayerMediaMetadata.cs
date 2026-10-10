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

        return new PlayerMediaMetadata(fallbackTitle, fallbackTitle, string.Empty, string.Empty, null, fallbackTitle, torrentHash);
    }
}
