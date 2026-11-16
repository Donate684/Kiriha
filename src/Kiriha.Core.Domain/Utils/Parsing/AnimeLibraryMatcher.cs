using System.Text.RegularExpressions;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Domain.Utils.Parsing;

/// <summary>
/// Shared deterministic release-title parsing, season/year disambiguation, and library candidate scoring
/// used across both <c>MappingService</c> (scrobbler/tracker matching) and <c>PlayerMediaMetadataResolver</c>.
/// </summary>
public static partial class AnimeLibraryMatcher
{
    [GeneratedRegex(@"[sS](\d{1,2})[eE]\d+|\b[sS]eason\s*(\d{1,2})\b|\b(\d{1,2})(?:st|nd|rd|th)\s+[sS]eason\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex SeasonRegex();

    [GeneratedRegex(@"\b(II|III|IV|V|VI)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex RomanSeasonSuffixRegex();

    [GeneratedRegex(@"\((19[6-9]\d|20\d{2})\)", RegexOptions.CultureInvariant)]
    public static partial Regex ParenthesizedYearRegex();

    public static int GetStatusPriority(UserAnimeStatus status) => status switch
    {
        UserAnimeStatus.Watching => 5,
        UserAnimeStatus.PlanToWatch => 4,
        UserAnimeStatus.OnHold => 3,
        UserAnimeStatus.Completed => 2,
        UserAnimeStatus.Dropped => 1,
        _ => 0
    };

    public static int ComputeYearBonus(AnimeEntity candidate, int? parsedYear)
    {
        if (!parsedYear.HasValue)
            return 0;

        int? cy = candidate.StartYear ?? candidate.AiringDate?.Year;
        if (!cy.HasValue)
            return 0;

        return cy.Value == parsedYear.Value
            ? 2
            : (Math.Abs(cy.Value - parsedYear.Value) == 1 ? 1 : 0);
    }

    public static int ExtractSeason(string rawTitle, string cleanTitle, int? parsedSeason)
    {
        if (parsedSeason is > 0)
            return parsedSeason.Value;

        var m = SeasonRegex().Match(rawTitle);
        if (m.Success)
        {
            string val = m.Groups[1].Success ? m.Groups[1].Value :
                         m.Groups[2].Success ? m.Groups[2].Value :
                         m.Groups[3].Value;
            if (int.TryParse(val, out int s) && s > 0)
                return s;
        }

        if (!string.IsNullOrWhiteSpace(cleanTitle))
        {
            var mc = SeasonRegex().Match(cleanTitle);
            if (mc.Success)
            {
                string val = mc.Groups[1].Success ? mc.Groups[1].Value :
                             mc.Groups[2].Success ? mc.Groups[2].Value :
                             mc.Groups[3].Value;
                if (int.TryParse(val, out int s) && s > 0)
                    return s;
            }

            var trimmed = cleanTitle.Trim().TrimEnd('.', '-', '_', ':', '~', ' ', '!', '?');
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

    public static bool HasSeasonSuffixOrToken(string extractedTitle)
    {
        if (string.IsNullOrWhiteSpace(extractedTitle))
            return false;

        var trimmed = extractedTitle.Trim().TrimEnd('.', '-', '_', ':', '~', ' ', '!', '?');
        return RomanSeasonSuffixRegex().IsMatch(trimmed) || SeasonRegex().IsMatch(extractedTitle);
    }

    public static string BuildSearchTitle(string cleanTitle, string? releaseType, int parsedSeason, int? parsedYear)
    {
        string searchTitle = cleanTitle;
        if (!string.IsNullOrEmpty(releaseType))
        {
            string rt = releaseType.ToUpperInvariant();
            if (rt is "OVA" or "OAD" or "SPECIAL" or "SP" or "ONA")
                searchTitle = $"{cleanTitle} {rt}";
        }

        if (searchTitle == cleanTitle && parsedSeason > 1 && !HasSeasonSuffixOrToken(cleanTitle))
            searchTitle = $"{cleanTitle} Season {parsedSeason}";

        if (searchTitle == cleanTitle && parsedYear.HasValue && !cleanTitle.Contains(parsedYear.Value.ToString(), StringComparison.Ordinal))
            searchTitle = $"{cleanTitle} ({parsedYear.Value})";

        return searchTitle;
    }

    public static bool IsCompatibleCandidate(AnimeEntity candidate, int? parsedEp, int? parsedYear, bool requireExplicitYear)
    {
        if (candidate.MediaKind != MediaKind.Anime)
            return false;

        int? ay = candidate.StartYear ?? candidate.AiringDate?.Year;
        if (parsedYear.HasValue)
        {
            if (ay.HasValue && Math.Abs(ay.Value - parsedYear.Value) > 1)
                return false;
            if (requireExplicitYear && !ay.HasValue)
                return false;
        }

        if (parsedEp.HasValue && candidate.TotalEpisodes > 1 && parsedEp.Value > candidate.TotalEpisodes)
            return false;

        return true;
    }

    public static AnimeEntity? FindBestMatch(
        IReadOnlyList<AnimeEntity> allAnimes,
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

        // 3. Bare extractedTitle fallback (only when not Season 2+ / OVA / ONA; if year was parsed, candidate must match year)
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

    public static bool MatchesExact(AnimeEntity a, string target)
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

    public static bool MatchesNormalized(AnimeEntity a, string normTarget)
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

    public static AnimeEntity? PickBestCandidate(
        IReadOnlyList<AnimeEntity> candidates,
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

            int yearBonus = ComputeYearBonus(c, parsedYear);
            int priority = GetStatusPriority(c.Status);

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
