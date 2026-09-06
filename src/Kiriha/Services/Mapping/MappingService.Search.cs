using Kiriha.Services.Data.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    public virtual async Task<int?> SearchOnMalAsync(string title)
    {
        var (cleanTitle, searchQuery, _, _) = ParseAnimeTitle(title);

        string normQuery = Normalize(searchQuery);
        if (_sessionCache.TryGetValue(normQuery, out int cachedId))
            return cachedId == 0 ? null : cachedId;

        var cachedMatches = _recognitionCache.Search(normQuery);
        if (cachedMatches != null)
        {
            var bestMatch = cachedMatches.OrderByDescending(m => m.Weight).FirstOrDefault();
            return bestMatch.Id != 0 ? bestMatch.Id : null;
        }

        // Persistent L2: DB-backed cache. Survives restarts so re-scanning the
        // same library doesn't re-hit MAL for queries we've resolved before.
        // GetMalSearchCacheAsync already enforces TTL (positive 30d, negative 7d)
        // and returns null on expired entries.
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
                    _sessionCache[normQuery] = dbHit.AnimeId; // promote to L1
                    return dbHit.AnimeId == 0 ? null : dbHit.AnimeId;
                }
            }
        }
        catch (Exception ex)
        {
            // Cache miss on error — fall through to live API. Don't let a
            // transient DB hiccup break title resolution.
            Log.Debug(ex, "MappingService: MAL search cache lookup failed for {Query}", normQuery);
        }

        var searchResults = await _malApi.SearchAnimeAsync(searchQuery);
        if (!searchResults.Any() && searchQuery != cleanTitle)
            searchResults = await _malApi.SearchAnimeAsync(cleanTitle);

        if (!searchResults.Any())
        {
            // Negative cache: avoid re-hitting MAL for the same unresolvable title.
            _sessionCache[normQuery] = 0;
            try { await _malSearchCache.UpsertAsync(normQuery, 0, 0f); }
            catch (Exception ex) { Log.Debug(ex, "MappingService: failed to persist negative MAL search cache"); }
            return null;
        }

        string normQ = Normalize(searchQuery);
        var queryWords = normQ.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var bestMalMatch = searchResults.Take(5)
            .Select((r, index) =>
            {
                float score = 0;

                var titles = new List<string> { r.Title };
                if (!string.IsNullOrEmpty(r.EnglishTitle)) titles.Add(r.EnglishTitle);
                if (!string.IsNullOrEmpty(r.JapaneseTitle)) titles.Add(r.JapaneseTitle);
                if (r.AlternativeTitles != null) titles.AddRange(r.AlternativeTitles);

                foreach (var t in titles)
                {
                    string normT = Normalize(t);
                    var titleWords = normT.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    int matchingWords = queryWords.Count(qw => titleWords.Contains(qw));
                    float currentScore = queryWords.Length > 0 ? (matchingWords / (float)queryWords.Length) * 70 : 0;

                    if (normT == normQ) currentScore = 100;

                    string[] criticalKeywords = { "movie", "ova", "oad", "special", "ii", "2", "iii", "3", "iv", "4", "v", "5" };
                    foreach (var word in criticalKeywords)
                    {
                        bool inQuery = queryWords.Contains(word, StringComparer.OrdinalIgnoreCase);
                        bool inTitle = titleWords.Contains(word, StringComparer.OrdinalIgnoreCase);

                        if (inQuery && inTitle) currentScore += 30;
                        else if (inQuery && !inTitle) currentScore -= 15;
                    }

                    if (currentScore > score) score = currentScore;
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
        try { await _malSearchCache.UpsertAsync(normQuery, resolvedId, bestMalMatch.Score); }
        catch (Exception ex) { Log.Debug(ex, "MappingService: failed to persist positive MAL search cache"); }

        return resolvedId;
    }
}
