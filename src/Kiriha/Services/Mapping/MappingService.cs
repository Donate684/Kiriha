using Kiriha.Services.Data.Mapping;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Utils.Parsing;

namespace Kiriha.Services.Data.Mapping;

public partial class MappingService : IMappingService
{
    private readonly IMalApiService _malApi;
    private readonly ManualMappingService _manualMapping;
    private readonly IMalSearchCacheRepository _malSearchCache;
    private readonly RecognitionCache _recognitionCache;
    private readonly ConcurrentDictionary<string, int> _sessionCache = new();
    private readonly ConcurrentDictionary<int, (string t, string e, string r)> _normalizedItemCache = new();

    private readonly Lock _indexLock = new();
    private WeakReference<IEnumerable<AnimeEntity>>? _cachedListRef;
    private UserListIndex? _cachedIndex;

    public MappingService(IMalApiService malApi, ManualMappingService manualMapping, IMalSearchCacheRepository malSearchCache, RecognitionCache recognitionCache)
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
        lock (_indexLock)
        {
            _cachedListRef = null;
            _cachedIndex = null;
        }
    }

    private UserListIndex GetOrBuildIndex(IEnumerable<AnimeEntity> userList)
    {
        lock (_indexLock)
        {
            if (_cachedListRef != null &&
                _cachedListRef.TryGetTarget(out var target) &&
                ReferenceEquals(target, userList) &&
                _cachedIndex != null)
            {
                return _cachedIndex;
            }

            var index = UserListIndex.Build(userList);
            _cachedListRef = new WeakReference<IEnumerable<AnimeEntity>>(userList);
            _cachedIndex = index;
            return index;
        }
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

        // Build or retrieve indexed lookup structures for userList
        var index = GetOrBuildIndex(userList);

        // 2. Recognition Cache
        string normSearch = Normalize(searchTitle);

        var cachedMatches = _recognitionCache.Search(normSearch);
        if (cachedMatches != null)
        {
            var matches = cachedMatches.OrderByDescending(m => m.Weight).ToList();
            foreach (var match in matches)
            {
                if (match.Id == 0) continue;
                var anime = index.FindById(match.Id);
                if (anime != null && !IsValidMatch(anime, parsedEpisode)) continue;

                _sessionCache[normalizedWithSeason] = match.Id;
                return match.Id;
            }
        }

        // 3. User List Exact Match
        var localMatch = index.FindExact(searchTitle, parsedEpisode, IsValidMatch);

        // Don't fall back to the bare title when a higher season was explicitly
        // parsed from the filename ("2nd Season", "S02", etc.) — otherwise we'd
        // happily match e.g. "Sousou no Frieren 2nd Season - 01" to the S1 entry
        // in the user list. Let SearchOnMalAsync handle these cases instead.
        if (localMatch == null && searchTitle != cleanTitle && parsedSeason <= 1)
        {
            localMatch = index.FindExact(cleanTitle, parsedEpisode, IsValidMatch);
        }

        if (localMatch != null && IsValidMatch(localMatch, parsedEpisode))
        {
            _sessionCache[normalizedWithSeason] = localMatch.Id;
            return localMatch.Id;
        }

        // 4. User List Normalized Match
        string normTitle = Normalize(cleanTitle);
        string normSearchTitle = Normalize(searchTitle);

        localMatch = index.FindNormalized(normSearchTitle, parsedEpisode, IsValidMatch);

        // Same season-aware guard as in step 3: never collapse a "Season 2+"
        // query down to the base title here, otherwise the normalized fallback
        // silently maps "Sousou no Frieren 2nd Season - 01" to the S1 entry.
        if (localMatch == null && normSearchTitle != normTitle && parsedSeason <= 1)
        {
            localMatch = index.FindNormalized(normTitle, parsedEpisode, IsValidMatch);
        }

        if (localMatch != null && IsValidMatch(localMatch, parsedEpisode))
        {
            _sessionCache[normalizedWithSeason] = localMatch.Id;
            return localMatch.Id;
        }

        return null;
    }

    private sealed class UserListIndex
    {
        private readonly Dictionary<int, AnimeEntity> _byId = new();
        private readonly Dictionary<string, List<AnimeEntity>> _exact = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<AnimeEntity>> _normalized = new(StringComparer.OrdinalIgnoreCase);

        public static UserListIndex Build(IEnumerable<AnimeEntity> items)
        {
            var index = new UserListIndex();
            foreach (var item in items)
            {
                index.Add(item);
            }
            return index;
        }

        private void Add(AnimeEntity item)
        {
            _byId.TryAdd(item.Id, item);

            AddExactAndNorm(item.Title, item);
            AddExactAndNorm(item.EnglishTitle, item);
            AddExactAndNorm(item.RussianTitle, item);
        }

        private void AddExactAndNorm(string? title, AnimeEntity item)
        {
            if (string.IsNullOrWhiteSpace(title)) return;

            if (!_exact.TryGetValue(title, out var list))
            {
                list = new List<AnimeEntity>(1);
                _exact[title] = list;
            }
            list.Add(item);

            string norm = AnimeStringHelper.Normalize(title);
            if (!string.IsNullOrEmpty(norm))
            {
                if (!_normalized.TryGetValue(norm, out var normList))
                {
                    normList = new List<AnimeEntity>(1);
                    _normalized[norm] = normList;
                }
                normList.Add(item);
            }
        }

        public AnimeEntity? FindById(int id)
        {
            _byId.TryGetValue(id, out var item);
            return item;
        }

        public AnimeEntity? FindExact(string title, int? episodeNumber, Func<AnimeEntity, int?, bool> isValidMatch)
        {
            if (_exact.TryGetValue(title, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (isValidMatch(list[i], episodeNumber)) return list[i];
                }
            }
            return null;
        }

        public AnimeEntity? FindNormalized(string normTitle, int? episodeNumber, Func<AnimeEntity, int?, bool> isValidMatch)
        {
            if (_normalized.TryGetValue(normTitle, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (isValidMatch(list[i], episodeNumber)) return list[i];
                }
            }
            return null;
        }
    }
}
