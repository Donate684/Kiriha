using System.Text.RegularExpressions;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Domain.Utils.Parsing;

public static partial class AnimeLibraryMatcher
{
    [GeneratedRegex(@"[sS](\d{1,2})[eE]\d+|\b[sS]eason\s*(\d{1,2})\b|\b(\d{1,2})(?:st|nd|rd|th)\s+[sS]eason\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex SeasonRegex();

    [GeneratedRegex(@"\b(II|III|IV|V|VI)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex RomanSeasonSuffixRegex();

    [GeneratedRegex(@"\((19[6-9]\d|20\d{2})\)", RegexOptions.CultureInvariant)]
    public static partial Regex ParenthesizedYearRegex();

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
}
