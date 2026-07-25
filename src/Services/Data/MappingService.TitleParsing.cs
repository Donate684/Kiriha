using System;
using System.IO;
using System.Linq;
using Kiriha.Models;

namespace Kiriha.Services.Data;

public partial class MappingService
{
    private bool IsValidMatch(AnimeItem match, int? episodeNumber)
    {
        if (episodeNumber == null) return true;
        if (match.TotalEpisodes <= 1) return true;
        if (episodeNumber > match.TotalEpisodes) return false;
        return true;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"[sS](\d{1,2})[eE]\d+|\b[sS]eason\s*(\d{1,2})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex SeasonRegex();

    private int ExtractSeason(string title, AnitomySharp.Element? seasonElement)
    {
        if (seasonElement != null && int.TryParse(seasonElement.Value, out int s)) return s;

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
        var titleElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle);
        var seasonElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeSeason);
        var typeElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeType);
        var subTitleElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeTitle);
        var otherElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementOther);
        var episodeElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber);

        int parsedSeason = ExtractSeason(title, seasonElement);
        int? parsedEpisode = null;
        if (episodeElement != null && int.TryParse(episodeElement.Value, out int ep))
        {
            parsedEpisode = ep;
        }

        string cleanTitle = titleElement != null ? titleElement.Value : Path.GetFileNameWithoutExtension(title);

        if (episodeElement == null)
        {
            if (subTitleElement != null && !cleanTitle.Contains(subTitleElement.Value, StringComparison.OrdinalIgnoreCase))
                cleanTitle = $"{cleanTitle} {subTitleElement.Value}";
            if (otherElement != null && !cleanTitle.Contains(otherElement.Value, StringComparison.OrdinalIgnoreCase))
                cleanTitle = $"{cleanTitle} {otherElement.Value}";
        }

        string searchTitle = cleanTitle;
        if (typeElement != null && !string.IsNullOrEmpty(typeElement.Value))
        {
            string type = typeElement.Value.ToUpperInvariant();
            if (type == "OVA" || type == "OAD" || type == "SPECIAL" || type == "SP" || type == "ONA")
                searchTitle = $"{cleanTitle} {type}";
        }

        if (searchTitle == cleanTitle && parsedSeason > 1)
            searchTitle = $"{cleanTitle} Season {parsedSeason}";

        return (cleanTitle, searchTitle, parsedSeason, parsedEpisode);
    }

    private string Normalize(string s) => Kiriha.Utils.Parsing.AnimeStringHelper.Normalize(s);
}
