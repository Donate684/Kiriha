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

    /// <summary>
    /// Matches a parsed RSS/release title against an active anime list, enforcing year and sequel/season boundaries.
    /// </summary>
    public static AnimeEntity? MatchActiveAnime(
        IReadOnlyList<AnimeEntity> activeAnime,
        string animeTitle,
        int? parsedSeason,
        int? parsedYear = null)
    {
        if (activeAnime.Count == 0 || string.IsNullOrWhiteSpace(animeTitle))
            return null;

        var parsedInput = AnimeParseCache.Parse(animeTitle);
        int? effectiveYear = parsedYear ?? parsedInput.Year;
        var cleanBaseTitle = effectiveYear.HasValue && !string.IsNullOrWhiteSpace(parsedInput.Title)
            ? parsedInput.Title
            : animeTitle;

        var normBase = NormalizeAlphaNum(cleanBaseTitle);
        if (string.IsNullOrEmpty(normBase))
            return null;

        var eligible = new List<AnimeEntity>(activeAnime.Count);
        for (int i = 0; i < activeAnime.Count; i++)
        {
            var candidate = activeAnime[i];
            if (candidate.MediaKind == MediaKind.Anime && IsYearCompatible(candidate, effectiveYear))
                eligible.Add(candidate);
        }

        if (eligible.Count == 0)
            return null;

        if (parsedSeason is > 1)
        {
            int s = parsedSeason.Value;
            string ordinal = GetOrdinalToken(s);
            string? romanNumeral = GetRomanNumeralToken(s);
            string[] seasonMarkers = [$"season{s}", $"{ordinal}season", $"s{s}", $"part{s}"];

            foreach (var candidate in eligible)
            {
                if (MatchesSequelCandidate(candidate.Title, normBase, seasonMarkers, s, romanNumeral) ||
                    MatchesSequelCandidate(candidate.EnglishTitle, normBase, seasonMarkers, s, romanNumeral) ||
                    MatchesSequelCandidate(candidate.RussianTitle, normBase, seasonMarkers, s, romanNumeral) ||
                    candidate.AlternativeTitles.Any(alt => MatchesSequelCandidate(alt, normBase, seasonMarkers, s, romanNumeral)))
                {
                    return candidate;
                }
            }

            return null;
        }

        if (effectiveYear.HasValue)
        {
            string yearQualified = $"{cleanBaseTitle} ({effectiveYear.Value})";
            string normWithYear = NormalizeAlphaNum(yearQualified);

            var yearMatch = eligible.FirstOrDefault(x =>
                string.Equals(x.Title, yearQualified, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.EnglishTitle, yearQualified, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.RussianTitle, yearQualified, StringComparison.OrdinalIgnoreCase) ||
                NormalizeAlphaNum(x.Title) == normWithYear ||
                NormalizeAlphaNum(x.EnglishTitle) == normWithYear ||
                NormalizeAlphaNum(x.RussianTitle) == normWithYear ||
                x.AlternativeTitles.Any(alt => NormalizeAlphaNum(alt) == normWithYear));
            if (yearMatch != null)
                return yearMatch;

            var explicitYearMatch = eligible.FirstOrDefault(x =>
                (x.StartYear ?? x.AiringDate?.Year) is int y &&
                Math.Abs(y - effectiveYear.Value) <= 1 &&
                MatchesAlphaNumBase(x, normBase));
            if (explicitYearMatch != null)
                return explicitYearMatch;

            return eligible.FirstOrDefault(x =>
                (x.StartYear ?? x.AiringDate?.Year) is null &&
                MatchesAlphaNumBase(x, normBase));
        }

        var exact = eligible.FirstOrDefault(x =>
            string.Equals(x.Title, cleanBaseTitle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.EnglishTitle, cleanBaseTitle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.RussianTitle, cleanBaseTitle, StringComparison.OrdinalIgnoreCase) ||
            x.AlternativeTitles.Any(alt => string.Equals(alt, cleanBaseTitle, StringComparison.OrdinalIgnoreCase)));
        if (exact != null)
            return exact;

        return eligible.FirstOrDefault(x => MatchesAlphaNumBase(x, normBase));
    }

    /// <summary>
    /// Verifies whether a selected library <see cref="AnimeEntity"/> is compatible with a torrent/stream release
    /// (year tolerance, Season 2+ markers, and episode upper bound).
    /// </summary>
    public static bool IsReleaseCompatibleWithAnime(
        AnimeEntity? selected,
        string primaryReleaseTitle,
        string? fallbackReleaseTitle = null,
        string? fallbackEpisode = null,
        bool enforceSeasonAndEpisode = true,
        string? activeSearchQuery = null)
    {
        if (selected is null || selected.MediaKind != MediaKind.Anime)
            return false;

        var parsed = AnimeParseCache.Parse(Path.GetFileNameWithoutExtension(primaryReleaseTitle));
        if (string.IsNullOrWhiteSpace(parsed.Title) && !string.IsNullOrWhiteSpace(fallbackReleaseTitle))
            parsed = AnimeParseCache.Parse(fallbackReleaseTitle);

        if (!IsYearCompatible(selected, parsed.Year))
            return false;

        if (enforceSeasonAndEpisode && parsed.Season is > 1)
        {
            int s = parsed.Season.Value;
            string seasonToken = s.ToString();
            string[] seasonWordTokens = GetSeasonWordTokens(s);

            bool hasSeasonMarker =
                TitleHasSeasonSignal(selected.Title, seasonToken, seasonWordTokens) ||
                TitleHasSeasonSignal(selected.EnglishTitle, seasonToken, seasonWordTokens) ||
                TitleHasSeasonSignal(selected.RussianTitle, seasonToken, seasonWordTokens) ||
                selected.AlternativeTitles.Any(alt => TitleHasSeasonSignal(alt, seasonToken, seasonWordTokens));
            if (!hasSeasonMarker)
                return false;
        }

        var epCandidate = !string.IsNullOrWhiteSpace(parsed.Episode) ? parsed.Episode : fallbackEpisode;
        if (enforceSeasonAndEpisode &&
            !string.IsNullOrWhiteSpace(activeSearchQuery) &&
            int.TryParse(epCandidate, out int epNum) &&
            selected.TotalEpisodes > 1 &&
            epNum > selected.TotalEpisodes &&
            !string.Equals(activeSearchQuery, selected.Title, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(activeSearchQuery, selected.EnglishTitle, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(activeSearchQuery, selected.RussianTitle, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public static bool IsYearCompatible(AnimeEntity candidate, int? parsedYear)
    {
        if (!parsedYear.HasValue)
            return true;

        int? cy = candidate.StartYear ?? candidate.AiringDate?.Year;
        return !cy.HasValue || Math.Abs(cy.Value - parsedYear.Value) <= 1;
    }

    private static bool MatchesAlphaNumBase(AnimeEntity candidate, string normBase) =>
        NormalizeAlphaNum(candidate.Title) == normBase ||
        NormalizeAlphaNum(candidate.EnglishTitle) == normBase ||
        NormalizeAlphaNum(candidate.RussianTitle) == normBase ||
        candidate.AlternativeTitles.Any(alt => NormalizeAlphaNum(alt) == normBase);

    private static string GetOrdinalToken(int season) => season switch
    {
        2 => "2nd",
        3 => "3rd",
        _ => $"{season}th"
    };

    private static string? GetRomanNumeralToken(int season) => season switch
    {
        2 => "ii",
        3 => "iii",
        4 => "iv",
        5 => "v",
        6 => "vi",
        _ => null
    };

    private static string[] GetSeasonWordTokens(int season) => season switch
    {
        2 => ["ii", "2nd", "second"],
        3 => ["iii", "3rd", "third"],
        4 => ["iv", "4th", "fourth"],
        5 => ["v", "5th", "fifth"],
        6 => ["vi", "6th", "sixth"],
        _ => [$"{season}th"]
    };

    private static bool TitleHasSeasonSignal(string? raw, string seasonToken, string[] seasonWordTokens)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var words = AnimeStringHelper.Normalize(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Contains(seasonToken))
            return true;

        foreach (var token in seasonWordTokens)
        {
            if (words.Contains(token))
                return true;
        }

        return raw.Contains(':', StringComparison.Ordinal) || raw.Contains(" - ", StringComparison.Ordinal);
    }

    private static bool MatchesSequelCandidate(
        string? candidateTitle,
        string normBase,
        string[] seasonMarkers,
        int seasonNumber,
        string? romanNumeral)
    {
        var normCandidate = NormalizeAlphaNum(candidateTitle);
        if (string.IsNullOrEmpty(normCandidate) || !normCandidate.StartsWith(normBase, StringComparison.Ordinal))
            return false;

        var tail = normCandidate[normBase.Length..];
        foreach (var marker in seasonMarkers)
        {
            if (tail.Contains(marker, StringComparison.Ordinal))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(candidateTitle))
        {
            var tokens = TokenizeAlphaNumWords(candidateTitle);
            int consumedChars = 0;
            int idx = 0;
            while (idx < tokens.Count && consumedChars < normBase.Length)
            {
                consumedChars += tokens[idx].Length;
                idx++;
            }

            if (consumedChars == normBase.Length)
            {
                string numStr = seasonNumber.ToString();
                for (int i = idx; i < tokens.Count; i++)
                {
                    if (tokens[i] == numStr || (romanNumeral != null && tokens[i] == romanNumeral))
                        return true;
                }
            }
        }

        return false;
    }

    private static List<string> TokenizeAlphaNumWords(string value)
    {
        var words = new List<string>();
        int start = -1;
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsLetterOrDigit(value[i]))
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                words.Add(value[start..i].ToLowerInvariant());
                start = -1;
            }
        }

        if (start >= 0)
            words.Add(value[start..].ToLowerInvariant());

        return words;
    }

    private static string NormalizeAlphaNum(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        Span<char> buf = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        int n = 0;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                buf[n++] = char.ToLowerInvariant(ch);
        }

        return new string(buf[..n]);
    }
}
