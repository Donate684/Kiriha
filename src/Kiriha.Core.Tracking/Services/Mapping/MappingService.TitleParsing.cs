using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Utils.Parsing;

namespace Kiriha.Services.Data.Mapping;

public partial class MappingService
{
    private bool IsValidMatch(AnimeEntity match, int? episodeNumber, int? parsedYear = null) =>
        AnimeLibraryMatcher.IsCompatibleCandidate(match, episodeNumber, parsedYear, requireExplicitYear: false);

    private bool IsValidBareTitleFallback(AnimeEntity match, int? episodeNumber, int? parsedYear) =>
        AnimeLibraryMatcher.IsCompatibleCandidate(match, episodeNumber, parsedYear, requireExplicitYear: parsedYear.HasValue);

    private (string CleanTitle, string SearchTitle, int ParsedSeason, int? ParsedEpisode, int? ParsedYear) ParseAnimeTitle(string title)
    {
        var parsed = AnimeParseCache.Parse(title);

        string cleanTitle = !string.IsNullOrEmpty(parsed.Title) ? parsed.Title : Path.GetFileNameWithoutExtension(title);
        int? parsedEpisode = int.TryParse(parsed.Episode, out int ep) ? ep : null;
        int parsedSeason = AnimeLibraryMatcher.ExtractSeason(title, cleanTitle, parsed.Season);
        int? parsedYear = parsed.Year;
        if (!parsedYear.HasValue)
        {
            var yearMatch = AnimeLibraryMatcher.ParenthesizedYearRegex().Match(title);
            if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out int y))
                parsedYear = y;
        }

        if (parsedEpisode is null && !string.IsNullOrEmpty(parsed.EpisodeTitle) && !cleanTitle.Contains(parsed.EpisodeTitle, StringComparison.OrdinalIgnoreCase))
        {
            cleanTitle = $"{cleanTitle} {parsed.EpisodeTitle}";
        }

        string searchTitle = AnimeLibraryMatcher.BuildSearchTitle(cleanTitle, parsed.ReleaseType, parsedSeason, parsedYear);
        return (cleanTitle, searchTitle, parsedSeason, parsedEpisode, parsedYear);
    }

    private string Normalize(string s) => AnimeStringHelper.Normalize(s);
}
