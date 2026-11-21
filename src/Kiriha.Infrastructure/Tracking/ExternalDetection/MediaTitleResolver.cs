using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Utils.Parsing;
using Kiriha.Infrastructure.Utils.Parsing;

namespace Kiriha.Infrastructure.Tracking.ExternalDetection;

/// <summary>
/// Centralized title parser and media resolver for window titles and file paths.
/// </summary>
public static class MediaTitleResolver
{
    public static ParsedMedia FromFilePath(string filePath, ExternalPlayerDefinition player)
    {
        string filename = Path.GetFileName(filePath);
        var parsed = AnimeParseCache.Parse(filename);

        string animeTitle;
        if (EmberTitleResolver.TryResolveDirectoryMetadata(filePath, out string emberTitle, out _, out _, out _))
        {
            animeTitle = emberTitle;
        }
        else if (!string.IsNullOrEmpty(parsed.Title))
        {
            animeTitle = parsed.Title;
        }
        else
        {
            string meaningfulDir = EmberTitleResolver.GetMeaningfulDirectoryName(filePath);
            if (!string.IsNullOrEmpty(meaningfulDir))
            {
                var dirParsed = AnimeParseCache.Parse(meaningfulDir);
                animeTitle = !string.IsNullOrEmpty(dirParsed.Title) ? dirParsed.Title : meaningfulDir;
            }
            else
            {
                animeTitle = Path.GetFileNameWithoutExtension(filename);
            }
        }

        return new ParsedMedia
        {
            OriginalTitle = filename,
            PlaybackSourceKey = ParsedMedia.CreatePlaybackSourceKey(filePath),
            AnimeTitle = animeTitle,
            EpisodeTitle = parsed.EpisodeTitle,
            Episode = parsed.Episode,
            Season = parsed.Season?.ToString() ?? string.Empty,
            Group = parsed.ReleaseGroup,
            IsPlaying = true,
            ProcessName = player.Executables.FirstOrDefault() ?? string.Empty
        };
    }

    public static ParsedMedia FromExtractedTitle(string extractedTitle)
    {
        var parsed = AnimeParseCache.Parse(extractedTitle);
        string animeTitle = !string.IsNullOrEmpty(parsed.Title) ? parsed.Title : extractedTitle;

        return new ParsedMedia
        {
            OriginalTitle = extractedTitle,
            AnimeTitle = animeTitle,
            EpisodeTitle = parsed.EpisodeTitle,
            Episode = parsed.Episode,
            Season = parsed.Season?.ToString() ?? string.Empty,
            Group = parsed.ReleaseGroup,
            IsPlaying = true
        };
    }
}
