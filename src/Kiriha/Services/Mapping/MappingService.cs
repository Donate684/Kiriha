using System.Collections.Concurrent;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
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
    private int _lastIndexedFingerprint;

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
        _recognitionCache.ClearLearnedMatches();
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
                if (_recognitionCache.IsIndexEmpty && _cachedIndex.Count > 0)
                {
                    _recognitionCache.BuildIndex(userList);
                    _lastIndexedFingerprint = _cachedIndex.Fingerprint;
                }
                return _cachedIndex;
            }

            var index = UserListIndex.Build(userList);
            if ((_recognitionCache.IsIndexEmpty && index.Count > 0) ||
                (_lastIndexedFingerprint != 0 && _lastIndexedFingerprint != index.Fingerprint) ||
                (_lastIndexedFingerprint == 0 && index.Count > 0))
            {
                _recognitionCache.BuildIndex(userList);
                _lastIndexedFingerprint = index.Fingerprint;
            }

            _cachedListRef = new WeakReference<IEnumerable<AnimeEntity>>(userList);
            _cachedIndex = index;
            return index;
        }
    }

    internal static int GetStatusPriority(UserAnimeStatus status) => status switch
    {
        UserAnimeStatus.Watching => 5,
        UserAnimeStatus.PlanToWatch => 4,
        UserAnimeStatus.OnHold => 3,
        UserAnimeStatus.Completed => 2,
        UserAnimeStatus.Dropped => 1,
        _ => 0
    };

    public virtual async Task<int?> GetIdFromTitleAsync(string title, IEnumerable<AnimeEntity> userList)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var (cleanTitle, searchTitle, parsedSeason, parsedEpisode, parsedYear) = ParseAnimeTitle(title);

        string normOriginal = Normalize(title);
        string normClean = Normalize(cleanTitle);
        string normSearch = Normalize(searchTitle);
        string rawSearchLower = searchTitle.Trim().ToLowerInvariant();
        string rawCleanLower = cleanTitle.Trim().ToLowerInvariant();

        bool isDistinctVariant = parsedSeason > 1 ||
                                 parsedYear.HasValue ||
                                 !string.Equals(normSearch, normClean, StringComparison.OrdinalIgnoreCase);

        // 1. Manual Mappings (highest priority — explicit user override)
        if (_manualMapping.TryGetMapping(normOriginal, out int id)) return id;
        if (_manualMapping.TryGetMapping(normSearch, out id)) return id;
        if (_manualMapping.TryGetMapping(rawSearchLower, out id)) return id;
        if (!isDistinctVariant)
        {
            if (_manualMapping.TryGetMapping(normClean, out id)) return id;
            if (_manualMapping.TryGetMapping(rawCleanLower, out id)) return id;
        }

        // Build or retrieve indexed lookup structures for userList
        var index = GetOrBuildIndex(userList);

        // 0. Session Cache (positive hits validated against current episode/year)
        if (_sessionCache.TryGetValue(normSearch, out id) && id > 0)
        {
            var cachedAnime = index.FindById(id);
            if (cachedAnime is null || IsValidMatch(cachedAnime, parsedEpisode, parsedYear))
                return id;
        }
        if (!isDistinctVariant && _sessionCache.TryGetValue(normClean, out id) && id > 0)
        {
            var cachedAnime = index.FindById(id);
            if (cachedAnime is null || IsValidMatch(cachedAnime, parsedEpisode, parsedYear))
                return id;
        }

        // 2. Recognition Cache
        var cachedMatches = _recognitionCache.Search(normSearch);
        if (cachedMatches != null)
        {
            var bestId = SelectBestRecognitionMatch(
                cachedMatches,
                index,
                parsedYear,
                a => IsValidMatch(a, parsedEpisode, parsedYear),
                requireInIndex: false);
            if (bestId.HasValue)
            {
                _sessionCache[normSearch] = bestId.Value;
                return bestId.Value;
            }
        }

        if (normSearch != normClean && parsedSeason <= 1)
        {
            var fallbackMatches = _recognitionCache.Search(normClean);
            if (fallbackMatches != null)
            {
                var bestFallbackId = SelectBestRecognitionMatch(
                    fallbackMatches,
                    index,
                    parsedYear,
                    a => IsValidBareTitleFallback(a, parsedEpisode, parsedYear),
                    requireInIndex: parsedYear.HasValue);
                if (bestFallbackId.HasValue)
                {
                    _sessionCache[normSearch] = bestFallbackId.Value;
                    return bestFallbackId.Value;
                }
            }
        }

        // 3. User List Exact Match
        var localMatch = index.FindExact(searchTitle, parsedEpisode, parsedYear, IsValidMatch);

        // Don't fall back to the bare title when a higher season was explicitly
        // parsed from the filename ("2nd Season", "S02", etc.), and when a year
        // was parsed ("(2026)"), only accept bare-title candidates whose year matches.
        if (localMatch is null && searchTitle != cleanTitle && parsedSeason <= 1)
        {
            localMatch = index.FindExact(cleanTitle, parsedEpisode, parsedYear, IsValidBareTitleFallback);
        }

        if (localMatch != null && IsValidMatch(localMatch, parsedEpisode, parsedYear))
        {
            _sessionCache[normSearch] = localMatch.Id;
            return localMatch.Id;
        }

        // 4. User List Normalized Match
        localMatch = index.FindNormalized(normSearch, parsedEpisode, parsedYear, IsValidMatch);

        if (localMatch is null && normSearch != normClean && parsedSeason <= 1)
        {
            localMatch = index.FindNormalized(normClean, parsedEpisode, parsedYear, IsValidBareTitleFallback);
        }

        if (localMatch != null && IsValidMatch(localMatch, parsedEpisode, parsedYear))
        {
            _sessionCache[normSearch] = localMatch.Id;
            return localMatch.Id;
        }

        return null;
    }

    private static int? SelectBestRecognitionMatch(
        IEnumerable<WeightedMatch> matches,
        UserListIndex index,
        int? parsedYear,
        Func<AnimeEntity, bool> validator,
        bool requireInIndex)
    {
        int? bestId = null;
        float bestWeight = float.MinValue;
        int bestYearBonus = int.MinValue;
        int bestStatusPriority = int.MinValue;

        foreach (var match in matches)
        {
            if (match.Id == 0) continue;

            var anime = index.FindById(match.Id);
            if (anime is null)
            {
                if (requireInIndex) continue;
            }
            else if (!validator(anime))
            {
                continue;
            }

            int yearBonus = 0;
            if (parsedYear.HasValue && anime != null)
            {
                int? my = anime.StartYear ?? anime.AiringDate?.Year;
                if (my.HasValue)
                    yearBonus = my.Value == parsedYear.Value ? 2 : (Math.Abs(my.Value - parsedYear.Value) == 1 ? 1 : 0);
            }

            int statusPriority = anime != null ? GetStatusPriority(anime.Status) : 0;

            bool isBetter = match.Weight > bestWeight ||
                            (Math.Abs(match.Weight - bestWeight) < 0.001f &&
                             (yearBonus > bestYearBonus ||
                              (yearBonus == bestYearBonus && statusPriority > bestStatusPriority)));

            if (isBetter)
            {
                bestId = match.Id;
                bestWeight = match.Weight;
                bestYearBonus = yearBonus;
                bestStatusPriority = statusPriority;
            }
        }

        return bestId;
    }

    public sealed class UserListIndex
    {
        private readonly Dictionary<int, AnimeEntity> _byId = new();
        private readonly Dictionary<string, List<AnimeEntity>> _exact = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<AnimeEntity>> _normalized = new(StringComparer.OrdinalIgnoreCase);

        public int Count => _byId.Count;
        public int Fingerprint { get; private set; }

        public static UserListIndex Build(IEnumerable<AnimeEntity> items)
        {
            var index = new UserListIndex();
            var hash = new HashCode();
            foreach (var item in items)
            {
                if (item.MediaKind != MediaKind.Anime)
                    continue;

                index.Add(item);
                hash.Add(item.Id);
                hash.Add(item.Title);
                hash.Add(item.StartYear ?? item.AiringDate?.Year ?? 0);
                hash.Add((int)item.Status);
            }
            index.Fingerprint = index.Count == 0 ? 0 : hash.ToHashCode();
            return index;
        }

        private void Add(AnimeEntity item)
        {
            _byId.TryAdd(item.Id, item);

            AddExactAndNorm(item.Title, item);
            AddExactAndNorm(item.EnglishTitle, item);
            AddExactAndNorm(item.RussianTitle, item);

            int? year = item.StartYear ?? item.AiringDate?.Year;
            if (year.HasValue)
            {
                string yearStr = year.Value.ToString();
                if (!string.IsNullOrWhiteSpace(item.Title) && !item.Title.Contains(yearStr, StringComparison.Ordinal))
                    AddExactAndNorm($"{item.Title} ({year.Value})", item);
                if (!string.IsNullOrWhiteSpace(item.EnglishTitle) && !item.EnglishTitle.Contains(yearStr, StringComparison.Ordinal))
                    AddExactAndNorm($"{item.EnglishTitle} ({year.Value})", item);
                if (!string.IsNullOrWhiteSpace(item.RussianTitle) && !item.RussianTitle.Contains(yearStr, StringComparison.Ordinal))
                    AddExactAndNorm($"{item.RussianTitle} ({year.Value})", item);
            }
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
            => FindExact(title, episodeNumber, parsedYear: null, (a, ep, _) => isValidMatch(a, ep));

        public AnimeEntity? FindExact(
            string title,
            int? episodeNumber,
            int? parsedYear,
            Func<AnimeEntity, int?, int?, bool> isValidMatch)
        {
            return _exact.TryGetValue(title, out var list)
                ? SelectBestCandidate(list, episodeNumber, parsedYear, isValidMatch)
                : null;
        }

        public AnimeEntity? FindNormalized(string normTitle, int? episodeNumber, Func<AnimeEntity, int?, bool> isValidMatch)
            => FindNormalized(normTitle, episodeNumber, parsedYear: null, (a, ep, _) => isValidMatch(a, ep));

        public AnimeEntity? FindNormalized(
            string normTitle,
            int? episodeNumber,
            int? parsedYear,
            Func<AnimeEntity, int?, int?, bool> isValidMatch)
        {
            return _normalized.TryGetValue(normTitle, out var list)
                ? SelectBestCandidate(list, episodeNumber, parsedYear, isValidMatch)
                : null;
        }

        private static AnimeEntity? SelectBestCandidate(
            List<AnimeEntity> list,
            int? episodeNumber,
            int? parsedYear,
            Func<AnimeEntity, int?, int?, bool> isValidMatch)
        {
            AnimeEntity? best = null;
            int bestYearBonus = int.MinValue;
            int bestPriority = int.MinValue;

            for (int i = 0; i < list.Count; i++)
            {
                var candidate = list[i];
                if (!isValidMatch(candidate, episodeNumber, parsedYear))
                    continue;

                int yearBonus = 0;
                if (parsedYear.HasValue)
                {
                    int? cy = candidate.StartYear ?? candidate.AiringDate?.Year;
                    if (cy.HasValue)
                        yearBonus = cy.Value == parsedYear.Value ? 2 : (Math.Abs(cy.Value - parsedYear.Value) == 1 ? 1 : 0);
                }

                int priority = GetStatusPriority(candidate.Status);
                if (best is null || yearBonus > bestYearBonus || (yearBonus == bestYearBonus && priority > bestPriority))
                {
                    best = candidate;
                    bestYearBonus = yearBonus;
                    bestPriority = priority;
                }
            }

            return best;
        }
    }
}
