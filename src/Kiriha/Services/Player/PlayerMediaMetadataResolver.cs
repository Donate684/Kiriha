using System.Text.RegularExpressions;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Core;
using Kiriha.Utils.Parsing;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Mpv.UI.Services.Player;

public sealed partial class PlayerMediaMetadataResolver : IPlayerMediaMetadataResolver
{
    private readonly IAnimeRepository? _animeRepository;
    private readonly IDbContextFactory<AppDbContext>? _dbFactory;

    [GeneratedRegex(@"([sS]?\d*[eE]\d+)\s*-(.*)")]
    private static partial Regex HyphenatedEpisodeRegex();

    public PlayerMediaMetadataResolver(
        IAnimeRepository animeRepository,
        IDbContextFactory<AppDbContext>? dbFactory = null)
    {
        _animeRepository = animeRepository;
        _dbFactory = dbFactory;
    }

    public PlayerMediaMetadataResolver(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    private IReadOnlyList<AnimeEntity> GetCandidateAnimes()
    {
        var inMemory = _animeRepository?.TryGetSnapshot();
        if (inMemory != null)
            return inMemory;

        if (_dbFactory != null)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.UserAnime.AsNoTracking()
                .Where(a => a.MediaKind == MediaKind.Anime)
                .ToList();
        }

        return [];
    }

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
                filename = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(seg));

                var query = uri.Query;
                if (!string.IsNullOrEmpty(query))
                {
                    var torrentMatch = Regex.Match(query, AppConstants.Torrents.TorrentHashLinkRegex);
                    if (torrentMatch.Success)
                    {
                        torrentHash = torrentMatch.Groups[1].Value;
                    }
                }
            }
            else
            {
                filename = Path.GetFileNameWithoutExtension(videoPath);
            }

            if (PlayerMediaMetadata.IsStreamUrl(videoPath) || PlayerMediaMetadata.IsStreamManifest(filename))
                return new PlayerMediaMetadata(string.Empty, string.Empty, string.Empty, string.Empty, null, string.Empty, torrentHash);

            var filenameToParse = HyphenatedEpisodeRegex().Replace(filename, "$1 - $2");
            var parsed = AnimeParseCache.Parse(filenameToParse);

            var extractedTitle = parsed.Title;
            var episodeText = parsed.Episode;
            int parsedSeason = parsed.Season ?? 0;
            int? parsedYear = parsed.Year;

            string originalTitle = filename;
            bool isEmber = videoPath.Contains("EMBER", StringComparison.OrdinalIgnoreCase) || EmberTitleResolver.ScanFileForEmber(videoPath);
            if (isEmber)
            {
                string meaningfulDir = EmberTitleResolver.GetMeaningfulDirectoryName(videoPath);
                if (!string.IsNullOrEmpty(meaningfulDir))
                {
                    var dirParsed = AnimeParseCache.Parse(meaningfulDir);
                    extractedTitle = !string.IsNullOrEmpty(dirParsed.Title) ? dirParsed.Title : meaningfulDir;
                    parsedYear ??= dirParsed.Year;
                    if (parsedSeason <= 0 && dirParsed.Season.HasValue)
                        parsedSeason = dirParsed.Season.Value;
                    originalTitle = meaningfulDir;
                }
            }

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

            string searchTitle = AnimeLibraryMatcher.BuildSearchTitle(extractedTitle, parsed.ReleaseType, parsedSeason, parsedYear);
            int? parsedEp = int.TryParse(episodeText, out int epVal) ? epVal : null;
            bool allowBareFallback = parsedSeason <= 1 && string.IsNullOrEmpty(parsed.ReleaseType);

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
