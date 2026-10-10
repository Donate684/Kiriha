using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Utils.Parsing;
using Serilog;

namespace Kiriha.Mpv.UI.Services.Player;

public sealed partial class FilenamePlayerMediaMetadataResolver : IPlayerMediaMetadataResolver
{
    [System.Text.RegularExpressions.GeneratedRegex(@"([sS]?\d*[eE]\d+)\s*-(.*)")]
    private static partial System.Text.RegularExpressions.Regex HyphenatedEpisodeRegex();

    public PlayerMediaMetadata Resolve(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            return PlayerMediaMetadata.FromVideoPath(videoPath);

        try
        {
            string filename;
            string torrentHash = string.Empty;

            if (Uri.TryCreate(videoPath, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                var seg = uri.Segments.LastOrDefault() ?? string.Empty;
                var rawName = System.IO.Path.GetFileNameWithoutExtension(seg);
                filename = Uri.UnescapeDataString(rawName);

                var query = uri.Query;
                if (!string.IsNullOrEmpty(query))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(query, AppConstants.Torrents.TorrentHashLinkRegex);
                    if (match.Success)
                    {
                        torrentHash = match.Groups[1].Value;
                    }
                }
            }
            else
            {
                filename = System.IO.Path.GetFileNameWithoutExtension(videoPath);
            }

            var filenameToParse = HyphenatedEpisodeRegex().Replace(filename, "$1 - $2");
            var parsed = AnimeParseCache.Parse(filenameToParse);

            string? title = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
            var episode = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value;

            string originalTitle = filename;
            bool isEmber = videoPath.Contains("EMBER", StringComparison.OrdinalIgnoreCase) || EmberTitleResolver.ScanFileForEmber(videoPath);
            if (isEmber)
            {
                string meaningfulDir = EmberTitleResolver.GetMeaningfulDirectoryName(videoPath);
                if (!string.IsNullOrEmpty(meaningfulDir))
                {
                    var dirParsed = AnimeParseCache.Parse(meaningfulDir);
                    var dirTitle = dirParsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
                    title = dirTitle ?? meaningfulDir;
                    originalTitle = meaningfulDir;
                }
            }

            var resolvedTitle = string.IsNullOrWhiteSpace(title) ? filename : title;
            return new PlayerMediaMetadata(
                originalTitle,
                resolvedTitle,
                string.Empty,
                episode ?? string.Empty,
                null,
                resolvedTitle,
                torrentHash);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse player media metadata from {Path}", videoPath);
            return PlayerMediaMetadata.FromVideoPath(videoPath);
        }
    }
}
