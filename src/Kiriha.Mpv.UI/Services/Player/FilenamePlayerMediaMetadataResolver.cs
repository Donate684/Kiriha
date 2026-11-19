using System.Text.RegularExpressions;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Utils.Parsing;
using Kiriha.Infrastructure.Utils.Parsing;
using Serilog;

namespace Kiriha.Mpv.UI.Services.Player;

internal readonly record struct ParsedVideoPathInfo(
    bool IsStreamManifest,
    string Filename,
    string OriginalTitle,
    string ExtractedTitle,
    string EpisodeText,
    string? ReleaseType,
    int ParsedSeason,
    int? ParsedYear,
    string TorrentHash);

public sealed partial class FilenamePlayerMediaMetadataResolver : IPlayerMediaMetadataResolver
{
    [GeneratedRegex(@"([sS]?\d*[eE]\d+)\s*-(.*)", RegexOptions.None, 500)]
    internal static partial Regex HyphenatedEpisodeRegex();

    internal static ParsedVideoPathInfo ExtractVideoPathInfo(string videoPath)
    {
        string filename;
        string torrentHash = string.Empty;

        if (Uri.TryCreate(videoPath, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var seg = uri.Segments.LastOrDefault() ?? string.Empty;
            filename = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(seg));

            var query = uri.Query;
            if (!string.IsNullOrEmpty(query))
            {
                var match = Regex.Match(query, AppConstants.Torrents.TorrentHashLinkRegex, RegexOptions.None, TimeSpan.FromMilliseconds(500));
                if (match.Success)
                {
                    torrentHash = match.Groups[1].Value;
                }
            }
        }
        else
        {
            filename = Path.GetFileNameWithoutExtension(videoPath);
        }

        if (PlayerMediaMetadata.IsStreamUrl(videoPath) || PlayerMediaMetadata.IsStreamManifest(filename))
        {
            return new ParsedVideoPathInfo(
                IsStreamManifest: true,
                Filename: filename,
                OriginalTitle: string.Empty,
                ExtractedTitle: string.Empty,
                EpisodeText: string.Empty,
                ReleaseType: null,
                ParsedSeason: 0,
                ParsedYear: null,
                TorrentHash: torrentHash);
        }

        var filenameToParse = HyphenatedEpisodeRegex().Replace(filename, "$1 - $2");
        var parsed = AnimeParseCache.Parse(filenameToParse);

        string extractedTitle = parsed.Title;
        string episode = parsed.Episode;
        int parsedSeason = parsed.Season ?? 0;
        int? parsedYear = parsed.Year;

        string originalTitle = filename;
        if (EmberTitleResolver.TryResolveDirectoryMetadata(videoPath, out string emberTitle, out string meaningfulDir, out int? dirYear, out int? dirSeason))
        {
            extractedTitle = emberTitle;
            parsedYear ??= dirYear;
            if (parsedSeason <= 0 && dirSeason.HasValue)
                parsedSeason = dirSeason.Value;
            originalTitle = meaningfulDir;
        }

        return new ParsedVideoPathInfo(
            IsStreamManifest: false,
            Filename: filename,
            OriginalTitle: originalTitle,
            ExtractedTitle: extractedTitle,
            EpisodeText: episode,
            ReleaseType: parsed.ReleaseType,
            ParsedSeason: parsedSeason,
            ParsedYear: parsedYear,
            TorrentHash: torrentHash);
    }

    public PlayerMediaMetadata Resolve(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            return PlayerMediaMetadata.FromVideoPath(videoPath);

        try
        {
            var info = ExtractVideoPathInfo(videoPath);
            if (info.IsStreamManifest)
            {
                return new PlayerMediaMetadata(string.Empty, string.Empty, string.Empty, string.Empty, null, string.Empty, info.TorrentHash);
            }

            var resolvedTitle = string.IsNullOrWhiteSpace(info.ExtractedTitle) ? info.Filename : info.ExtractedTitle;
            if (info.ParsedYear.HasValue && !string.IsNullOrWhiteSpace(info.ExtractedTitle) && !resolvedTitle.Contains(info.ParsedYear.Value.ToString(), StringComparison.Ordinal))
            {
                resolvedTitle = $"{resolvedTitle} ({info.ParsedYear.Value})";
            }

            return new PlayerMediaMetadata(
                info.OriginalTitle,
                resolvedTitle,
                string.Empty,
                info.EpisodeText,
                null,
                resolvedTitle,
                info.TorrentHash);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse player media metadata from {Path}", videoPath);
            return PlayerMediaMetadata.FromVideoPath(videoPath);
        }
    }
}
