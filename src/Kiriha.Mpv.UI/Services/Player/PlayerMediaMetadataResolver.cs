using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Utils.Parsing;
using Serilog;

namespace Kiriha.Mpv.UI.Services.Player;

public sealed class PlayerMediaMetadataResolver : IPlayerMediaMetadataResolver
{
    private readonly IAnimeRepository _animeRepository;

    public PlayerMediaMetadataResolver(IAnimeRepository animeRepository)
    {
        ArgumentNullException.ThrowIfNull(animeRepository);
        _animeRepository = animeRepository;
    }

    private IReadOnlyList<AnimeEntity> GetCandidateAnimes()
    {
        return _animeRepository.TryGetSnapshot() ?? [];
    }

    public PlayerMediaMetadata Resolve(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            return PlayerMediaMetadata.FromVideoPath(videoPath);

        try
        {
            var info = FilenamePlayerMediaMetadataResolver.ExtractVideoPathInfo(videoPath);
            if (info.IsStreamManifest)
                return new PlayerMediaMetadata(string.Empty, string.Empty, string.Empty, string.Empty, null, string.Empty, info.TorrentHash);

            var extractedTitle = info.ExtractedTitle;
            var episodeText = info.EpisodeText;
            var originalTitle = info.OriginalTitle;
            var torrentHash = info.TorrentHash;
            int parsedSeason = info.ParsedSeason;
            int? parsedYear = info.ParsedYear;

            if (!parsedYear.HasValue)
            {
                var ym = AnimeLibraryMatcher.ParenthesizedYearRegex().Match(originalTitle);
                if (ym.Success && int.TryParse(ym.Groups[1].Value, out int y))
                    parsedYear = y;
            }

            if (string.IsNullOrWhiteSpace(extractedTitle))
                return PlayerMediaMetadata.FromVideoPath(videoPath) with { EpisodeText = episodeText, TorrentHash = torrentHash };

            parsedSeason = AnimeLibraryMatcher.ExtractSeason(originalTitle, extractedTitle, parsedSeason);

            var displayExtractedTitle = parsedYear.HasValue && !extractedTitle.Contains(parsedYear.Value.ToString(), StringComparison.Ordinal)
                ? $"{extractedTitle} ({parsedYear.Value})"
                : extractedTitle;

            string searchTitle = AnimeLibraryMatcher.BuildSearchTitle(extractedTitle, info.ReleaseType, parsedSeason, parsedYear);
            int? parsedEp = int.TryParse(episodeText, out int epVal) ? epVal : null;
            bool allowBareFallback = parsedSeason <= 1 && string.IsNullOrEmpty(info.ReleaseType);

            var allAnimes = GetCandidateAnimes();
            var match = AnimeLibraryMatcher.FindBestMatch(allAnimes, searchTitle, extractedTitle, allowBareFallback, parsedEp, parsedYear);

            return match is null
                ? new PlayerMediaMetadata(originalTitle, displayExtractedTitle, string.Empty, episodeText, null, displayExtractedTitle, torrentHash)
                : new PlayerMediaMetadata(
                    originalTitle,
                    match.RussianTitle ?? match.Title ?? displayExtractedTitle,
                    match.EnglishTitle ?? match.Title ?? string.Empty,
                    episodeText,
                    match.Id,
                    match.Title ?? displayExtractedTitle,
                    torrentHash);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve player media metadata for {Path}", videoPath);
            return PlayerMediaMetadata.FromVideoPath(videoPath);
        }
    }

    public PlayerMediaMetadata? ResolveByTitle(string title, string? episodeText = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        try
        {
            var rawTitle = title.Trim();
            var parsed = AnimeParseCache.Parse(rawTitle);

            var extractedTitle = !string.IsNullOrWhiteSpace(parsed.Title) ? parsed.Title : rawTitle;
            int parsedSeason = AnimeLibraryMatcher.ExtractSeason(rawTitle, extractedTitle, parsed.Season);
            int? parsedYear = parsed.Year;
            if (!parsedYear.HasValue)
            {
                var ym = AnimeLibraryMatcher.ParenthesizedYearRegex().Match(rawTitle);
                if (ym.Success && int.TryParse(ym.Groups[1].Value, out int y))
                    parsedYear = y;
            }

            var effectiveEpText = !string.IsNullOrWhiteSpace(episodeText) ? episodeText : parsed.Episode;
            int? parsedEp = int.TryParse(effectiveEpText, out int epVal) ? epVal : null;

            var displayExtractedTitle = parsedYear.HasValue && !extractedTitle.Contains(parsedYear.Value.ToString(), StringComparison.Ordinal)
                ? $"{extractedTitle} ({parsedYear.Value})"
                : extractedTitle;

            string searchTitle = AnimeLibraryMatcher.BuildSearchTitle(extractedTitle, parsed.ReleaseType, parsedSeason, parsedYear);
            bool allowBareFallback = parsedSeason <= 1 && string.IsNullOrEmpty(parsed.ReleaseType);

            var allAnimes = GetCandidateAnimes();
            var match = AnimeLibraryMatcher.FindBestMatch(allAnimes, searchTitle, extractedTitle, allowBareFallback, parsedEp, parsedYear);
            if (match != null)
            {
                return new PlayerMediaMetadata(
                    rawTitle,
                    match.RussianTitle ?? match.Title ?? displayExtractedTitle,
                    match.EnglishTitle ?? match.Title ?? string.Empty,
                    episodeText ?? string.Empty,
                    match.Id,
                    match.Title ?? displayExtractedTitle);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve metadata by title for '{Title}'", title);
        }

        return null;
    }
}
