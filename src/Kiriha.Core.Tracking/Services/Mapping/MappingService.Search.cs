using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Services.Data.Mapping;

public partial class MappingService
{
    /// <summary>
    /// Minimum score a MAL search candidate must achieve before we accept it
    /// as a match. Prevents low-confidence false positives such as
    /// "Dota Dragons Blood" → "Kuutei Dragons" (~23 points).
    /// </summary>
    private const float MinConfidenceScore = 50f;
    private static readonly string[] CriticalKeywords = ["movie", "ova", "oad", "special", "ii", "2", "iii", "3", "iv", "4", "v", "5"];

    public virtual async Task<int?> SearchOnMalAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var (cleanTitle, searchQuery, _, parsedEpisode, parsedYear) = ParseAnimeTitle(title);

        string normQuery = Normalize(searchQuery);
        UserListIndex? currentIndex;
        lock (_indexLock) currentIndex = _cachedIndex;

        if (_sessionCache.TryGetValue(normQuery, out int cachedId))
        {
            if (cachedId == 0)
                return null;

            var sessIndexed = currentIndex?.FindById(cachedId);
            if (sessIndexed == null || IsValidMatch(sessIndexed, parsedEpisode, parsedYear))
                return cachedId;
        }

        var cachedMatches = _recognitionCache.Search(normQuery);
        if (cachedMatches != null)
        {
            foreach (var m in cachedMatches.OrderByDescending(m => m.Weight))
            {
                if (m.Id == 0) continue;
                var indexed = currentIndex?.FindById(m.Id);
                if (indexed != null && !IsValidMatch(indexed, parsedEpisode, parsedYear))
                    continue;

                return m.Id;
            }
        }

        // Persistent L2: DB-backed cache. Survives restarts so re-scanning the
        // same library doesn't re-hit MAL for queries we've resolved before.
        // GetMalSearchCacheAsync already enforces TTL (positive 30d, negative 7d)
        // and returns null on expired entries.
        if (_malSearchCache != null)
        {
            try
            {
                var dbHit = await _malSearchCache.GetAsync(normQuery);
                if (dbHit != null)
                {
                    // Reject cached entries that scored below our confidence threshold.
                    // These are stale false-positive matches from before the threshold
                    // was introduced (e.g. "Dota Dragons Blood" → Kuutei Dragons ~23).
                    // Invalidate the entry so future sessions re-resolve via live API.
                    if (dbHit.AnimeId != 0 && dbHit.Score < MinConfidenceScore)
                    {
                        Log.Information(
                            "MappingService: evicting low-confidence DB cache entry for '{Query}' (AnimeId={Id}, Score={Score:F1})",
                            normQuery, dbHit.AnimeId, dbHit.Score);
                        try { await _malSearchCache.UpsertAsync(normQuery, 0, 0f); }
                        catch (Exception ex) { Log.Debug(ex, "MappingService: failed to evict bad cache entry for {Query}", normQuery); }
                        // Fall through to live API lookup.
                    }
                    else
                    {
                        if (dbHit.AnimeId == 0)
                        {
                            _sessionCache[normQuery] = 0;
                            return null;
                        }

                        var dbIndexed = currentIndex?.FindById(dbHit.AnimeId);
                        if (dbIndexed == null || IsValidMatch(dbIndexed, parsedEpisode, parsedYear))
                        {
                            _sessionCache[normQuery] = dbHit.AnimeId; // promote to L1
                            return dbHit.AnimeId;
                        }
                        // Otherwise fall through to live API for episode/year disambiguation.
                    }
                }
            }
            catch (Exception ex)
            {
                // Cache miss on error — fall through to live API. Don't let a
                // transient DB hiccup break title resolution.
                Log.Debug(ex, "MappingService: MAL search cache lookup failed for {Query}", normQuery);
            }
        }

        if (_malApi is null)
            return null;

        var searchResults = (await _malApi.SearchAnimeAsync(searchQuery))
            .Where(r => r.MediaKind == MediaKind.Anime)
            .ToList();

        bool needCleanFallback = searchQuery != cleanTitle &&
            (searchResults.Count == 0 ||
             (parsedYear.HasValue && !searchResults.Any(r =>
                 (r.StartYear ?? r.AiringDate?.Year) is int y && Math.Abs(y - parsedYear.Value) <= 1)));

        if (needCleanFallback)
        {
            var cleanResults = (await _malApi.SearchAnimeAsync(cleanTitle))
                .Where(r => r.MediaKind == MediaKind.Anime);
            var seenIds = searchResults.Select(r => r.Id).ToHashSet();
            foreach (var cr in cleanResults)
            {
                if (seenIds.Add(cr.Id))
                    searchResults.Add(cr);
            }
        }

        if (searchResults.Count == 0)
        {
            // Negative cache: avoid re-hitting MAL for the same unresolvable title.
            _sessionCache[normQuery] = 0;
            if (_malSearchCache != null)
            {
                try { await _malSearchCache.UpsertAsync(normQuery, 0, 0f); }
                catch (Exception ex) { Log.Debug(ex, "MappingService: failed to persist negative MAL search cache"); }
            }
            return null;
        }

        string normQ = Normalize(searchQuery);
        var queryWords = normQ.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var bestMalMatch = searchResults.Take(10)
            .Index()
            .Select(entry =>
            {
                var (index, r) = entry;
                float score = 0;

                List<string> titles = [r.Title];
                if (!string.IsNullOrEmpty(r.EnglishTitle)) titles.Add(r.EnglishTitle);
                if (!string.IsNullOrEmpty(r.JapaneseTitle)) titles.Add(r.JapaneseTitle);
                if (r.AlternativeTitles != null) titles.AddRange(r.AlternativeTitles);

                int? candidateYear = r.StartYear ?? r.AiringDate?.Year;
                if (candidateYear.HasValue)
                {
                    string ys = candidateYear.Value.ToString();
                    if (!string.IsNullOrEmpty(r.Title) && !r.Title.Contains(ys, StringComparison.Ordinal))
                        titles.Add($"{r.Title} ({ys})");
                    if (!string.IsNullOrEmpty(r.EnglishTitle) && !r.EnglishTitle.Contains(ys, StringComparison.Ordinal))
                        titles.Add($"{r.EnglishTitle} ({ys})");
                }

                foreach (var t in titles)
                {
                    string normT = Normalize(t);
                    var titleWords = normT.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    int matchingWords = queryWords.Count(qw => titleWords.Contains(qw));
                    float currentScore = queryWords.Length > 0 ? (matchingWords / (float)queryWords.Length) * 70 : 0;

                    if (normT == normQ) currentScore = 100;

                    foreach (var word in CriticalKeywords)
                    {
                        bool inQuery = queryWords.Contains(word, StringComparer.OrdinalIgnoreCase);
                        bool inTitle = titleWords.Contains(word, StringComparer.OrdinalIgnoreCase);

                        if (inQuery && inTitle) currentScore += 30;
                        else if (inQuery && !inTitle) currentScore -= 15;
                    }

                    if (currentScore > score) score = currentScore;
                }

                if (parsedYear.HasValue)
                {
                    if (candidateYear.HasValue)
                    {
                        int yearDiff = Math.Abs(candidateYear.Value - parsedYear.Value);
                        if (yearDiff == 0) score += 15;
                        else if (yearDiff == 1) score += 5;
                        else score -= 50;
                    }
                    else
                    {
                        string ys = parsedYear.Value.ToString();
                        bool titleHasExplicitYear = titles.Any(t => t.Contains(ys, StringComparison.Ordinal));
                        if (!titleHasExplicitYear)
                            score -= 20;
                    }
                }

                if (parsedEpisode.HasValue && r.TotalEpisodes > 1 && parsedEpisode.Value > r.TotalEpisodes)
                {
                    score -= 35;
                }

                return new { Result = r, Score = score, Index = index };
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .First();

        // Require a minimum confidence score before accepting the match.
        // A low score (e.g. only one word overlaps out of three) means MAL
        // returned something superficially similar but almost certainly wrong
        // (e.g. "Dota Dragons Blood" → "Kuutei Dragons" scores ~23).
        // In that case, return null so the UI can honestly show "not found"
        // and let the user pick manually — the same as Taiga's "НЕТ" state.
        if (bestMalMatch.Score < MinConfidenceScore)
        {
            Log.Information(
                "MappingService: best MAL candidate '{Title}' scored {Score:F1} < {Min} for query '{Query}' — rejecting as low-confidence",
                bestMalMatch.Result.Title, bestMalMatch.Score, MinConfidenceScore, searchQuery);

            // Negative-cache only in session memory — don't persist to DB so
            // that a future manual search on a fresh session can still resolve it.
            _sessionCache[normQuery] = 0;
            return null;
        }

        var resolvedId = bestMalMatch.Result.Id;
        _sessionCache[normQuery] = resolvedId;
        _recognitionCache.AddMatch(normQuery, resolvedId, bestMalMatch.Score);

        // Persist to DB so future sessions skip the MAL round-trip.
        if (_malSearchCache != null)
        {
            try { await _malSearchCache.UpsertAsync(normQuery, resolvedId, bestMalMatch.Score); }
            catch (Exception ex) { Log.Debug(ex, "MappingService: failed to persist positive MAL search cache"); }
        }

        return resolvedId;
    }
}
