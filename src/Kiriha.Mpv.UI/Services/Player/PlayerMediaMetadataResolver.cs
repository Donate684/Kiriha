using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Core;
using Kiriha.Utils.Parsing;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Mpv.UI.Services.Player;

public interface IPlayerMediaMetadataResolver
{
    PlayerMediaMetadata Resolve(string videoPath);
    PlayerMediaMetadata? ResolveByTitle(string title, string? episodeText = null) => null;
}

public sealed partial class PlayerMediaMetadataResolver : IPlayerMediaMetadataResolver
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    [System.Text.RegularExpressions.GeneratedRegex(@"([sS]?\d*[eE]\d+)\s*-(.*)")]
    private static partial System.Text.RegularExpressions.Regex HyphenatedEpisodeRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(II|III|IV|V|VI)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex RomanSeasonSuffixRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"[sS](\d{1,2})[eE]\d+|\b[sS]eason\s*(\d{1,2})\b|\b(\d{1,2})(?:st|nd|rd|th)\s+[sS]eason\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex SeasonRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"\((19[6-9]\d|20\d{2})\)", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex ParenthesizedYearRegex();

    public PlayerMediaMetadataResolver(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
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
                filename = Uri.UnescapeDataString(System.IO.Path.GetFileNameWithoutExtension(seg));

                var query = uri.Query;
                if (!string.IsNullOrEmpty(query))
                {
                    var torrentMatch = System.Text.RegularExpressions.Regex.Match(query, Kiriha.Core.Domain.Constants.AppConstants.Torrents.TorrentHashLinkRegex);
                    if (torrentMatch.Success)
                    {
                        torrentHash = torrentMatch.Groups[1].Value;
                    }
                }
            }
            else
            {
                filename = System.IO.Path.GetFileNameWithoutExtension(videoPath);
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
                var ym = ParenthesizedYearRegex().Match(originalTitle);
                if (ym.Success && int.TryParse(ym.Groups[1].Value, out int y))
                    parsedYear = y;
            }

            if (string.IsNullOrWhiteSpace(extractedTitle))
                return PlayerMediaMetadata.FromVideoPath(videoPath) with { EpisodeText = episodeText, TorrentHash = torrentHash };

            parsedSeason = ResolveEffectiveSeason(originalTitle, extractedTitle, parsedSeason);

            var trimmed = extractedTitle.Trim().TrimEnd('.', '-', '_', ':', '~', ' ', '!', '?');
            bool extractedAlreadyHasSeason = RomanSeasonSuffixRegex().IsMatch(trimmed) || SeasonRegex().IsMatch(extractedTitle);

            var displayExtractedTitle = parsedYear.HasValue && !extractedTitle.Contains(parsedYear.Value.ToString(), StringComparison.Ordinal)
                ? $"{extractedTitle} ({parsedYear.Value})"
                : extractedTitle;

            string searchTitle = extractedTitle;
            if (!string.IsNullOrEmpty(parsed.ReleaseType))
            {
                string rt = parsed.ReleaseType.ToUpperInvariant();
                if (rt is "OVA" or "OAD" or "SPECIAL" or "SP" or "ONA")
                    searchTitle = $"{extractedTitle} {rt}";
            }
            if (searchTitle == extractedTitle && parsedSeason > 1 && !extractedAlreadyHasSeason)
                searchTitle = $"{extractedTitle} Season {parsedSeason}";
            if (searchTitle == extractedTitle && parsedYear.HasValue)
                searchTitle = displayExtractedTitle;

            int? parsedEp = int.TryParse(episodeText, out int epVal) ? epVal : null;
            bool allowBareFallback = parsedSeason <= 1 && string.IsNullOrEmpty(parsed.ReleaseType);

            using var db = _dbFactory.CreateDbContext();
            var allAnimes = db.UserAnime.AsNoTracking()
                .Where(a => a.MediaKind == MediaKind.Anime)
                .ToList();

            var match = FindBestAnimeMatch(allAnimes, searchTitle, extractedTitle, allowBareFallback, parsedEp, parsedYear);

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
            int parsedSeason = ResolveEffectiveSeason(rawTitle, extractedTitle, parsed.Season ?? 0);
            int? parsedYear = parsed.Year;
            if (!parsedYear.HasValue)
            {
                var ym = ParenthesizedYearRegex().Match(rawTitle);
                if (ym.Success && int.TryParse(ym.Groups[1].Value, out int y))
                    parsedYear = y;
            }

            var effectiveEpText = !string.IsNullOrWhiteSpace(episodeText) ? episodeText : parsed.Episode;
            int? parsedEp = int.TryParse(effectiveEpText, out int epVal) ? epVal : null;

            var trimmed = extractedTitle.Trim().TrimEnd('.', '-', '_', ':', '~', ' ', '!', '?');
            bool extractedAlreadyHasSeason = RomanSeasonSuffixRegex().IsMatch(trimmed) || SeasonRegex().IsMatch(extractedTitle);

            var displayExtractedTitle = parsedYear.HasValue && !extractedTitle.Contains(parsedYear.Value.ToString(), StringComparison.Ordinal)
                ? $"{extractedTitle} ({parsedYear.Value})"
                : extractedTitle;

            string searchTitle = extractedTitle;
            if (!string.IsNullOrEmpty(parsed.ReleaseType))
            {
                string rt = parsed.ReleaseType.ToUpperInvariant();
                if (rt is "OVA" or "OAD" or "SPECIAL" or "SP" or "ONA")
                    searchTitle = $"{extractedTitle} {rt}";
            }
            if (searchTitle == extractedTitle && parsedSeason > 1 && !extractedAlreadyHasSeason)
                searchTitle = $"{extractedTitle} Season {parsedSeason}";
            if (searchTitle == extractedTitle && parsedYear.HasValue)
                searchTitle = displayExtractedTitle;

            bool allowBareFallback = parsedSeason <= 1 && string.IsNullOrEmpty(parsed.ReleaseType);

            using var db = _dbFactory.CreateDbContext();
            var allAnimes = db.UserAnime.AsNoTracking()
                .Where(a => a.MediaKind == MediaKind.Anime)
                .ToList();

            var match = FindBestAnimeMatch(allAnimes, searchTitle, extractedTitle, allowBareFallback, parsedEp, parsedYear);
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

    private static int ResolveEffectiveSeason(string rawTitle, string extractedTitle, int parsedSeason)
    {
        if (parsedSeason > 0)
            return parsedSeason;

        var m = SeasonRegex().Match(rawTitle);
        if (m.Success)
        {
            string val = m.Groups[1].Success ? m.Groups[1].Value : (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);
            if (int.TryParse(val, out int s) && s > 0)
                return s;
        }

        if (!string.IsNullOrWhiteSpace(extractedTitle))
        {
            var mc = SeasonRegex().Match(extractedTitle);
            if (mc.Success)
            {
                string val = mc.Groups[1].Success ? mc.Groups[1].Value : (mc.Groups[2].Success ? mc.Groups[2].Value : mc.Groups[3].Value);
                if (int.TryParse(val, out int s) && s > 0)
                    return s;
            }

            var trimmed = extractedTitle.Trim().TrimEnd('.', '-', '_', ':', '~', ' ', '!', '?');
            var romanMatch = RomanSeasonSuffixRegex().Match(trimmed);
            if (romanMatch.Success)
            {
                return romanMatch.Groups[1].Value.ToUpperInvariant() switch
                {
                    "II" => 2,
                    "III" => 3,
                    "IV" => 4,
                    "V" => 5,
                    "VI" => 6,
                    _ => 0
                };
            }
        }

        return 0;
    }

    private static AnimeEntity? FindBestAnimeMatch(
        List<AnimeEntity> allAnimes,
        string searchTitle,
        string extractedTitle,
        bool allowBareFallback,
        int? parsedEp,
        int? parsedYear)
    {
        if (allAnimes.Count == 0)
            return null;

        // 1. Exact case-insensitive match on searchTitle (including synthetic "Title (StartYear)")
        var match = PickBestCandidate(
            allAnimes,
            a => MatchesExact(a, searchTitle) && IsCompatibleCandidate(a, parsedEp, parsedYear, requireExplicitYear: false),
            parsedYear);
        if (match != null)
            return match;

        // 2. Normalized match on searchTitle
        string normSearch = AnimeStringHelper.Normalize(searchTitle);
        if (!string.IsNullOrEmpty(normSearch))
        {
            match = PickBestCandidate(
                allAnimes,
                a => MatchesNormalized(a, normSearch) && IsCompatibleCandidate(a, parsedEp, parsedYear, requireExplicitYear: false),
                parsedYear);
            if (match != null)
                return match;
        }

        // 3. Bare extractedTitle fallback (only when not a Season 2+ / OVA / ONA, and if year was parsed, candidate must explicitly match year)
        if (allowBareFallback && !string.Equals(searchTitle, extractedTitle, StringComparison.OrdinalIgnoreCase))
        {
            match = PickBestCandidate(
                allAnimes,
                a => MatchesExact(a, extractedTitle) && IsCompatibleCandidate(a, parsedEp, parsedYear, requireExplicitYear: parsedYear.HasValue),
                parsedYear);
            if (match != null)
                return match;

            string normExtracted = AnimeStringHelper.Normalize(extractedTitle);
            if (!string.IsNullOrEmpty(normExtracted) && normExtracted != normSearch)
            {
                match = PickBestCandidate(
                    allAnimes,
                    a => MatchesNormalized(a, normExtracted) && IsCompatibleCandidate(a, parsedEp, parsedYear, requireExplicitYear: parsedYear.HasValue),
                    parsedYear);
                if (match != null)
                    return match;
            }
        }

        return null;
    }

    private static bool MatchesExact(AnimeEntity a, string target)
    {
        if (string.Equals(a.Title, target, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.EnglishTitle, target, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.RussianTitle, target, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        int? year = a.StartYear ?? a.AiringDate?.Year;
        if (year.HasValue)
        {
            string ys = year.Value.ToString();
            if (!string.IsNullOrEmpty(a.Title) && !a.Title.Contains(ys, StringComparison.Ordinal) &&
                string.Equals($"{a.Title} ({ys})", target, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrEmpty(a.EnglishTitle) && !a.EnglishTitle.Contains(ys, StringComparison.Ordinal) &&
                string.Equals($"{a.EnglishTitle} ({ys})", target, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrEmpty(a.RussianTitle) && !a.RussianTitle.Contains(ys, StringComparison.Ordinal) &&
                string.Equals($"{a.RussianTitle} ({ys})", target, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool MatchesNormalized(AnimeEntity a, string normTarget)
    {
        if ((!string.IsNullOrEmpty(a.Title) && AnimeStringHelper.Normalize(a.Title) == normTarget) ||
            (!string.IsNullOrEmpty(a.EnglishTitle) && AnimeStringHelper.Normalize(a.EnglishTitle) == normTarget) ||
            (!string.IsNullOrEmpty(a.RussianTitle) && AnimeStringHelper.Normalize(a.RussianTitle) == normTarget))
        {
            return true;
        }

        if (a.AlternativeTitles != null)
        {
            for (int i = 0; i < a.AlternativeTitles.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(a.AlternativeTitles[i]) &&
                    AnimeStringHelper.Normalize(a.AlternativeTitles[i]) == normTarget)
                {
                    return true;
                }
            }
        }

        int? year = a.StartYear ?? a.AiringDate?.Year;
        if (year.HasValue)
        {
            string ys = year.Value.ToString();
            if (!string.IsNullOrEmpty(a.Title) && !a.Title.Contains(ys, StringComparison.Ordinal) &&
                AnimeStringHelper.Normalize($"{a.Title} ({ys})") == normTarget)
                return true;
            if (!string.IsNullOrEmpty(a.EnglishTitle) && !a.EnglishTitle.Contains(ys, StringComparison.Ordinal) &&
                AnimeStringHelper.Normalize($"{a.EnglishTitle} ({ys})") == normTarget)
                return true;
            if (!string.IsNullOrEmpty(a.RussianTitle) && !a.RussianTitle.Contains(ys, StringComparison.Ordinal) &&
                AnimeStringHelper.Normalize($"{a.RussianTitle} ({ys})") == normTarget)
                return true;
        }

        return false;
    }

    private static bool IsCompatibleCandidate(AnimeEntity a, int? parsedEp, int? parsedYear, bool requireExplicitYear)
    {
        if (a.MediaKind != MediaKind.Anime)
            return false;

        int? ay = a.StartYear ?? a.AiringDate?.Year;
        if (parsedYear.HasValue)
        {
            if (ay.HasValue && Math.Abs(ay.Value - parsedYear.Value) > 1)
                return false;
            if (requireExplicitYear && !ay.HasValue)
                return false;
        }

        if (parsedEp.HasValue && a.TotalEpisodes > 1 && parsedEp.Value > a.TotalEpisodes)
            return false;

        return true;
    }

    private static AnimeEntity? PickBestCandidate(
        List<AnimeEntity> candidates,
        Func<AnimeEntity, bool> predicate,
        int? parsedYear)
    {
        AnimeEntity? best = null;
        int bestYearBonus = int.MinValue;
        int bestPriority = int.MinValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (!predicate(c))
                continue;

            int yearBonus = 0;
            if (parsedYear.HasValue)
            {
                int? cy = c.StartYear ?? c.AiringDate?.Year;
                if (cy.HasValue)
                    yearBonus = cy.Value == parsedYear.Value ? 2 : (Math.Abs(cy.Value - parsedYear.Value) == 1 ? 1 : 0);
            }

            int priority = c.Status switch
            {
                UserAnimeStatus.Watching => 5,
                UserAnimeStatus.PlanToWatch => 4,
                UserAnimeStatus.OnHold => 3,
                UserAnimeStatus.Completed => 2,
                UserAnimeStatus.Dropped => 1,
                _ => 0
            };

            if (best is null || yearBonus > bestYearBonus || (yearBonus == bestYearBonus && priority > bestPriority))
            {
                best = c;
                bestYearBonus = yearBonus;
                bestPriority = priority;
            }
        }

        return best;
    }
}
