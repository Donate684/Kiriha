using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Domain.Utils.Parsing;

/// <summary>
/// Shared deterministic release-title parsing, season/year disambiguation, and library candidate scoring
/// used across both <c>MappingService</c> (scrobbler/tracker matching) and <c>PlayerMediaMetadataResolver</c>.
/// </summary>
public static partial class AnimeLibraryMatcher
{
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

    public static bool IsYearCompatible(AnimeEntity candidate, int? parsedYear)
    {
        if (!parsedYear.HasValue)
            return true;

        int? cy = candidate.StartYear ?? candidate.AiringDate?.Year;
        return !cy.HasValue || Math.Abs(cy.Value - parsedYear.Value) <= 1;
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

        // 1. Exact case-insensitive match on searchTitle
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

        // 3. Bare extractedTitle fallback
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
