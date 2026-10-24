using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Services.Data.Mapping;

public partial class MappingService
{
    private bool IsValidMatch(AnimeEntity match, int? episodeNumber)
    {
        if (episodeNumber is null) return true;
        if (match.TotalEpisodes <= 1) return true;
        if (episodeNumber > match.TotalEpisodes) return false;
        return true;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"[sS](\d{1,2})[eE]\d+|\b[sS]eason\s*(\d{1,2})\b|\b(\d{1,2})(?:st|nd|rd|th)\s+[sS]eason\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex SeasonRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(II|III|IV|V|VI)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex RomanSeasonSuffixRegex();

    private int ExtractSeason(string title, string cleanTitle, int? parsedSeason)
    {
        if (parsedSeason is > 0) return parsedSeason.Value;

        var match = SeasonRegex().Match(title);
        if (match.Success)
        {
            string val = match.Groups[1].Success ? match.Groups[1].Value :
                         match.Groups[2].Success ? match.Groups[2].Value :
                         match.Groups[3].Value;
            if (int.TryParse(val, out int season) && season > 0)
                return season;
        }

        if (!string.IsNullOrWhiteSpace(cleanTitle))
        {
            var matchClean = SeasonRegex().Match(cleanTitle);
            if (matchClean.Success)
            {
                string val = matchClean.Groups[1].Success ? matchClean.Groups[1].Value :
                             matchClean.Groups[2].Success ? matchClean.Groups[2].Value :
                             matchClean.Groups[3].Value;
                if (int.TryParse(val, out int season) && season > 0)
                    return season;
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

    private (string CleanTitle, string SearchTitle, int ParsedSeason, int? ParsedEpisode) ParseAnimeTitle(string title)
    {
        var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(title);

        string cleanTitle = !string.IsNullOrEmpty(parsed.Title) ? parsed.Title : Path.GetFileNameWithoutExtension(title);
        int? parsedEpisode = int.TryParse(parsed.Episode, out int ep) ? ep : null;
        int parsedSeason = ExtractSeason(title, cleanTitle, parsed.Season);

        if (parsedEpisode is null && !string.IsNullOrEmpty(parsed.EpisodeTitle) && !cleanTitle.Contains(parsed.EpisodeTitle, StringComparison.OrdinalIgnoreCase))
        {
            cleanTitle = $"{cleanTitle} {parsed.EpisodeTitle}";
        }

        string searchTitle = cleanTitle;
        if (!string.IsNullOrEmpty(parsed.ReleaseType))
        {
            string type = parsed.ReleaseType.ToUpperInvariant();
            if (type == "OVA" || type == "OAD" || type == "SPECIAL" || type == "SP" || type == "ONA")
                searchTitle = $"{cleanTitle} {type}";
        }

        var trimmedClean = cleanTitle.Trim().TrimEnd('.', '-', '_', ':', '~', ' ', '!', '?');
        bool cleanTitleAlreadyHasSeason = RomanSeasonSuffixRegex().IsMatch(trimmedClean) ||
                                          SeasonRegex().IsMatch(cleanTitle);

        if (searchTitle == cleanTitle && parsedSeason > 1 && !cleanTitleAlreadyHasSeason)
            searchTitle = $"{cleanTitle} Season {parsedSeason}";

        return (cleanTitle, searchTitle, parsedSeason, parsedEpisode);
    }

    private string Normalize(string s) => Kiriha.Utils.Parsing.AnimeStringHelper.Normalize(s);
}
