using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Domain.Utils.Parsing;

public static partial class AnimeLibraryMatcher
{
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
