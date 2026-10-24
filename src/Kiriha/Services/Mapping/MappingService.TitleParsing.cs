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

    [System.Text.RegularExpressions.GeneratedRegex(@"[sS](\d{1,2})[eE]\d+|\b[sS]eason\s*(\d{1,2})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex SeasonRegex();

    private int ExtractSeason(string title, int? parsedSeason)
    {
        if (parsedSeason is > 0) return parsedSeason.Value;

        var match = SeasonRegex().Match(title);
        if (match.Success)
        {
            int.TryParse(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value, out int season);
            return season;
        }
        return 0;
    }

    private (string CleanTitle, string SearchTitle, int ParsedSeason, int? ParsedEpisode) ParseAnimeTitle(string title)
    {
        var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(title);

        int parsedSeason = ExtractSeason(title, parsed.Season);
        int? parsedEpisode = int.TryParse(parsed.Episode, out int ep) ? ep : null;

        string cleanTitle = !string.IsNullOrEmpty(parsed.Title) ? parsed.Title : Path.GetFileNameWithoutExtension(title);

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

        if (searchTitle == cleanTitle && parsedSeason > 1)
            searchTitle = $"{cleanTitle} Season {parsedSeason}";

        return (cleanTitle, searchTitle, parsedSeason, parsedEpisode);
    }

    private string Normalize(string s) => Kiriha.Utils.Parsing.AnimeStringHelper.Normalize(s);
}
