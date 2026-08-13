using Kiriha.Services.Data.Mapping;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Kiriha.Core.Abstractions.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Services;

namespace Kiriha.Services.Data.Mapping;

public partial class MappingService : IMappingService
{
    private readonly Kiriha.Core.Services.IMalApiService _malApi;
    private readonly ManualMappingService _manualMapping;
    private readonly Kiriha.Core.Repositories.IMalSearchCacheRepository _malSearchCache;
    private readonly RecognitionCache _recognitionCache;
    private readonly ConcurrentDictionary<string, int> _sessionCache = new();
    private readonly ConcurrentDictionary<int, (string t, string e, string r)> _normalizedItemCache = new();

    public MappingService(Kiriha.Core.Services.IMalApiService malApi, ManualMappingService manualMapping, Kiriha.Core.Repositories.IMalSearchCacheRepository malSearchCache, RecognitionCache recognitionCache)
    {
        _malApi = malApi;
        _manualMapping = manualMapping;
        _malSearchCache = malSearchCache;
        _recognitionCache = recognitionCache;
    }

    public void ClearRecognitionCaches()
    {
        _sessionCache.Clear();
        _recognitionCache.Clear();
        _normalizedItemCache.Clear();
    }

    public virtual async Task<int?> GetIdFromTitleAsync(string title, IEnumerable<AnimeEntity> userList)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var (cleanTitle, searchTitle, parsedSeason, parsedEpisode) = ParseAnimeTitle(title);

        string normalized = cleanTitle.Trim().ToLowerInvariant();
        string normalizedWithSeason = searchTitle.Trim().ToLowerInvariant();

        // 0. Session Cache (0 = cached negative result)
        if (_sessionCache.TryGetValue(normalizedWithSeason, out int id)) return id == 0 ? null : id;
        if (normalizedWithSeason != normalized && _sessionCache.TryGetValue(normalized, out id)) return id == 0 ? null : id;

        // 1. Manual Mappings
        string normOriginal = Normalize(title);
        string normClean = Normalize(cleanTitle);

        if (_manualMapping.TryGetMapping(normOriginal, out id)) return id;
        if (_manualMapping.TryGetMapping(normClean, out id)) return id;
        if (_manualMapping.TryGetMapping(normalizedWithSeason, out id)) return id;
        if (normalizedWithSeason != normalized && _manualMapping.TryGetMapping(normalized, out id)) return id;

        // 2. Recognition Cache
        string normSearch = Normalize(searchTitle);

        var cachedMatches = _recognitionCache.Search(normSearch);
        if (cachedMatches != null)
        {
            var matches = cachedMatches.OrderByDescending(m => m.Weight).ToList();
            foreach (var match in matches)
            {
                if (match.Id == 0) continue;
                var anime = userList.FirstOrDefault(x => x.Id == match.Id);
                if (anime != null && !IsValidMatch(anime, parsedEpisode)) continue;

                _sessionCache[normalizedWithSeason] = match.Id;
                return match.Id;
            }
        }

        // 3. User List Exact Match
        var localMatch = userList.FirstOrDefault(x =>
            string.Equals(x.Title, searchTitle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.EnglishTitle, searchTitle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.RussianTitle, searchTitle, StringComparison.OrdinalIgnoreCase));

        // Don't fall back to the bare title when a higher season was explicitly
        // parsed from the filename ("2nd Season", "S02", etc.) — otherwise we'd
        // happily match e.g. "Sousou no Frieren 2nd Season - 01" to the S1 entry
        // in the user list. Let SearchOnMalAsync handle these cases instead.
        if (localMatch == null && searchTitle != cleanTitle && parsedSeason <= 1)
        {
            localMatch = userList.FirstOrDefault(x =>
                string.Equals(x.Title, cleanTitle, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.EnglishTitle, cleanTitle, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.RussianTitle, cleanTitle, StringComparison.OrdinalIgnoreCase));
        }

        if (localMatch != null && IsValidMatch(localMatch, parsedEpisode))
        {
            _sessionCache[normalizedWithSeason] = localMatch.Id;
            return localMatch.Id;
        }

        // 4. User List Normalized Match
        string normTitle = Normalize(cleanTitle);
        string normSearchTitle = Normalize(searchTitle);

        localMatch = userList.FirstOrDefault(x =>
        {
            var cached = _normalizedItemCache.GetOrAdd(x.Id, _ => (
                Normalize(x.Title),
                Normalize(x.EnglishTitle ?? ""),
                Normalize(x.RussianTitle ?? "")
            ));
            return cached.t == normSearchTitle || cached.e == normSearchTitle || cached.r == normSearchTitle;
        });

        // Same season-aware guard as in step 3: never collapse a "Season 2+"
        // query down to the base title here, otherwise the normalized fallback
        // silently maps "Sousou no Frieren 2nd Season - 01" to the S1 entry.
        if (localMatch == null && normSearchTitle != normTitle && parsedSeason <= 1)
        {
            localMatch = userList.FirstOrDefault(x =>
            {
                var cached = _normalizedItemCache.GetOrAdd(x.Id, _ => (
                    Normalize(x.Title),
                    Normalize(x.EnglishTitle ?? ""),
                    Normalize(x.RussianTitle ?? "")
                ));
                return cached.t == normTitle || cached.e == normTitle || cached.r == normTitle;
            });
        }

        if (localMatch != null && IsValidMatch(localMatch, parsedEpisode))
        {
            _sessionCache[normalizedWithSeason] = localMatch.Id;
            return localMatch.Id;
        }

        return null;
    }
}
