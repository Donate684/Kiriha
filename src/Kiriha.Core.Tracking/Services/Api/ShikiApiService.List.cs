using System.Text;
using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

public partial class ShikiApiService
{
    private async Task<int?> GetEffectiveUserIdAsync(CancellationToken ct)
    {
        var acc = _settingsService.Current.Api.GetAccount(TrackerId);
        if (acc?.Tokens is ShikiTokens accTokens && accTokens.UserId.HasValue)
        {
            return accTokens.UserId.Value;
        }

        if (_settingsService.Current.Api.Shiki?.UserId != null)
        {
            return _settingsService.Current.Api.Shiki.UserId;
        }

        var userId = await GetCurrentUserIdAsync(ct);
        if (userId.HasValue)
        {
            _settingsService.Update(settings =>
            {
                var targetAcc = settings.Api.GetAccount(TrackerId);
                if (targetAcc?.Tokens is ShikiTokens st)
                {
                    st.UserId = userId;
                }
                if (settings.Api.Shiki != null)
                {
                    settings.Api.Shiki.UserId = userId;
                }
            }, save: false);
            _settingsService.SaveImmediate();
        }

        return userId;
    }

    public async Task<List<AnimeEntity>?> GetUserAnimeListAsync(CancellationToken ct = default)
    {
        Log.Information("Syncing user anime list from {Tracker}...", Name);
        return await FetchUserRatesAsync("Anime", ct);
    }

    public async Task<List<AnimeEntity>?> GetUserMangaListAsync(CancellationToken ct = default)
    {
        Log.Information("Syncing user manga list from {Tracker}...", Name);
        return await FetchUserRatesAsync("Manga", ct);
    }

    private async Task<List<AnimeEntity>?> FetchUserRatesAsync(string targetType, CancellationToken ct)
    {
        var token = await _tokenService.EnsureValidTokenAsync(_fixedMirror, ct);
        if (string.IsNullOrEmpty(token))
        {
            Log.Warning("ShikiApiService ({Tracker}): not authenticated, skipping list sync", Name);
            return null;
        }

        var userId = await GetEffectiveUserIdAsync(ct);
        if (userId is null)
        {
            Log.Warning("ShikiApiService ({Tracker}): failed to resolve user ID", Name);
            return null;
        }

        var list = new List<AnimeEntity>();
        int page = 1;
        // Use a large limit to try to fetch the entire list in one request.
        // Shikimori v2/user_rates supports up to 5000 per page.
        const int limit = 5000;
        // Hard per-page timeout: if a single page doesn't complete in this time,
        // we abort the sync rather than hang indefinitely. This is on top of the
        // per-request 20s timeout inside ResilientHttpHandler — it guards against
        // stalled TCP connections that survive the request-level timeout.
        const int PageTimeoutSeconds = 90;

        var endpoint = targetType.Equals("Manga", StringComparison.OrdinalIgnoreCase)
            ? "manga_rates"
            : "anime_rates";

        try
        {
            while (true)
            {
                var url = $"users/{userId.Value}/{endpoint}?page={page}&limit={limit}&censored=false";
                Log.Information("ShikiApiService ({Tracker}): fetching {Type} rates from {Endpoint} page {Page}...", Name, targetType, endpoint, page);

                HttpResponseMessage response;
                using var pageCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                pageCts.CancelAfter(TimeSpan.FromSeconds(PageTimeoutSeconds));
                try
                {
                    response = await GetAsync(url, pageCts.Token);
                    Log.Information("ShikiApiService ({Tracker}): page {Page} response HTTP {Status}", Name, page, (int)response.StatusCode);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Our page-level timeout fired, not the caller's cancellation
                    Log.Error("ShikiApiService ({Tracker}): page {Page} timed out after {Timeout}s — aborting sync", Name, page, PageTimeoutSeconds);
                    return null;
                }

                if (!response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.NotFound && page == 1)
                {
                    Log.Warning("ShikiApiService ({Tracker}): {Endpoint} returned 404, falling back to v2/user_rates...", Name, endpoint);
                    url = $"v2/user_rates?user_id={userId.Value}&target_type={targetType}&page={page}&limit={limit}";
                    response.Dispose();
                    response = await GetAsync(url, pageCts.Token);
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Warning("ShikiApiService ({Tracker}): page {Page} returned HTTP {Status}; aborting sync to avoid partial list overwrite.",
                            Name, page, response.StatusCode);
                        return null;
                    }

                    Log.Information("ShikiApiService ({Tracker}): reading body of page {Page}...", Name, page);
                    using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var doc = await JsonDocument.ParseAsync(stream, default, ct);
                    Log.Information("ShikiApiService ({Tracker}): parsed JSON for page {Page}", Name, page);

                    if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        Log.Warning("ShikiApiService ({Tracker}): unexpected non-array response on page {Page}", Name, page);
                        break;
                    }

                    int countInPage = 0;
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        countInPage++;
                        var anime = ShikiMapper.MapRateToAnimeEntity(
                            element,
                            ShikiWebsiteRoot,
                            (primaryId, shikiId) => _malToShikiMap[primaryId] = shikiId);

                        list.Add(anime);

                        if (element.TryGetProperty("id", out var rateIdProp) && rateIdProp.TryGetInt32(out var rateId))
                        {
                            var typeKey = targetType.Equals("Manga", StringComparison.OrdinalIgnoreCase) ? "Manga" : "Anime";
                            _userRateMap[$"{typeKey}_{anime.Id}"] = rateId;
                            if (element.TryGetProperty("target_id", out var tidProp) && tidProp.TryGetInt32(out var tid))
                            {
                                _userRateMap[$"{typeKey}_{tid}"] = rateId;
                            }
                        }
                    }

                    Log.Information("ShikiApiService ({Tracker}): page {Page} yielded {Count} items (running total: {Total})", Name, page, countInPage, list.Count);

                    if (countInPage < limit)
                    {
                        // Last page reached
                        break;
                    }
                }

                page++;
            }

            if (EffectiveMirror == ShikiMirror.One && list.Count > 0)
            {
                await EnrichMissingPostersFromGraphQlAsync(list, targetType.Equals("Manga", StringComparison.OrdinalIgnoreCase), ct);
            }

            Log.Information("ShikiApiService ({Tracker}): fetched {Count} {Type} items", Name, list.Count, targetType);
            return list;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "ShikiApiService ({Tracker}): failed to fetch user rates for {Type}", Name, targetType);
            return null;
        }
    }

    public async Task<SyncOutcome> UpdateProgressAsync(int animeId, int episodes, UserAnimeStatus? status = null, int? score = null, bool? isRewatching = null, int? rewatchCount = null, CancellationToken ct = default)
    {
        var token = await _tokenService.EnsureValidTokenAsync(_fixedMirror, ct);
        if (string.IsNullOrEmpty(token)) return SyncOutcome.PermanentFailure;

        var userId = await GetEffectiveUserIdAsync(ct);
        if (userId is null) return SyncOutcome.TransientFailure;

        var targetId = ResolveShikiTargetId(animeId);
        var userRate = new Dictionary<string, object>
        {
            ["user_id"] = userId.Value,
            ["target_id"] = targetId,
            ["target_type"] = "Anime",
            ["episodes"] = episodes
        };

        var shikiStatus = StatusMapper.ToShiki(status);
        if (!string.IsNullOrEmpty(shikiStatus)) userRate["status"] = shikiStatus;
        if (score.HasValue && score.Value > 0) userRate["score"] = score.Value;
        if (isRewatching.HasValue) userRate["is_rewatching"] = isRewatching.Value;
        if (rewatchCount.HasValue) userRate["rewatches"] = rewatchCount.Value;

        var payload = new { user_rate = userRate };
        return await PostAsync("v2/user_rates", payload, ct);
    }

    public async Task<SyncOutcome> SaveFullListStatusAsync(AnimeEntity item, CancellationToken ct = default)
    {
        var token = await _tokenService.EnsureValidTokenAsync(_fixedMirror, ct);
        if (string.IsNullOrEmpty(token)) return SyncOutcome.PermanentFailure;

        var userId = await GetEffectiveUserIdAsync(ct);
        if (userId is null) return SyncOutcome.TransientFailure;

        bool isManga = item.MediaKind != MediaKind.Anime;
        var targetId = ResolveShikiTargetId(item.Id);
        var userRate = new Dictionary<string, object>
        {
            ["user_id"] = userId.Value,
            ["target_id"] = targetId,
            ["target_type"] = isManga ? "Manga" : "Anime"
        };

        if (isManga)
        {
            userRate["chapters"] = item.ChaptersRead;
            userRate["volumes"] = item.VolumesRead;
        }
        else
        {
            userRate["episodes"] = item.Progress;
        }

        var shikiStatus = StatusMapper.ToShiki(item.Status);
        if (!string.IsNullOrEmpty(shikiStatus)) userRate["status"] = shikiStatus;
        if (int.TryParse(item.Score, out var score) && score > 0) userRate["score"] = score;
        userRate["is_rewatching"] = item.IsRewatching;
        userRate["rewatches"] = item.RewatchCount;

        var payload = new { user_rate = userRate };
        return await PostAsync("v2/user_rates", payload, ct);
    }

    public async Task<SyncOutcome> UpdateMangaProgressAsync(int mangaId, int chapters, int? volumes = null, UserAnimeStatus? status = null, int? score = null, CancellationToken ct = default)
    {
        var token = await _tokenService.EnsureValidTokenAsync(_fixedMirror, ct);
        if (string.IsNullOrEmpty(token)) return SyncOutcome.PermanentFailure;

        var userId = await GetEffectiveUserIdAsync(ct);
        if (userId is null) return SyncOutcome.TransientFailure;

        var targetId = ResolveShikiTargetId(mangaId);
        var userRate = new Dictionary<string, object>
        {
            ["user_id"] = userId.Value,
            ["target_id"] = targetId,
            ["target_type"] = "Manga",
            ["chapters"] = chapters
        };

        if (volumes.HasValue) userRate["volumes"] = volumes.Value;

        var shikiStatus = StatusMapper.ToShiki(status);
        if (!string.IsNullOrEmpty(shikiStatus)) userRate["status"] = shikiStatus;

        if (score.HasValue && score.Value > 0) userRate["score"] = score.Value;

        var payload = new { user_rate = userRate };
        return await PostAsync("v2/user_rates", payload, ct);
    }

    public async Task<SyncOutcome> RemoveAnimeAsync(int animeId, CancellationToken ct = default)
    {
        var token = await _tokenService.EnsureValidTokenAsync(_fixedMirror, ct);
        if (string.IsNullOrEmpty(token)) return SyncOutcome.PermanentFailure;

        var userId = await GetEffectiveUserIdAsync(ct);
        if (userId is null) return SyncOutcome.TransientFailure;

        var targetId = ResolveShikiTargetId(animeId);

        // 1. Check in-memory user_rate map
        int? userRateId = null;
        if (_userRateMap.TryGetValue($"Anime_{targetId}", out var cachedRateId) ||
            _userRateMap.TryGetValue($"Anime_{animeId}", out cachedRateId) ||
            _userRateMap.TryGetValue($"Manga_{targetId}", out cachedRateId) ||
            _userRateMap.TryGetValue($"Manga_{animeId}", out cachedRateId))
        {
            userRateId = cachedRateId;
        }

        // 2. Query Shikimori API if not found in memory
        if (userRateId == null)
        {
            userRateId = await FindUserRateIdAsync(userId.Value, targetId, "Anime", ct);
            if (userRateId == null && targetId != animeId)
            {
                userRateId = await FindUserRateIdAsync(userId.Value, animeId, "Anime", ct);
            }
            if (userRateId == null)
            {
                userRateId = await FindUserRateIdAsync(userId.Value, targetId, "Manga", ct);
                if (userRateId == null && targetId != animeId)
                {
                    userRateId = await FindUserRateIdAsync(userId.Value, animeId, "Manga", ct);
                }
            }
        }

        if (userRateId == null)
        {
            Log.Information("ShikiApiService ({Tracker}): entry for anime {AnimeId} not found on Shikimori; treated as already removed.", Name, animeId);
            return SyncOutcome.Success;
        }

        Log.Information("ShikiApiService ({Tracker}): deleting user rate {RateId} for anime {AnimeId}...", Name, userRateId.Value, animeId);
        var outcome = await DeleteAsync($"v2/user_rates/{userRateId.Value}", ct);
        if (outcome == SyncOutcome.Success)
        {
            _userRateMap.TryRemove($"Anime_{targetId}", out _);
            _userRateMap.TryRemove($"Anime_{animeId}", out _);
            _userRateMap.TryRemove($"Manga_{targetId}", out _);
            _userRateMap.TryRemove($"Manga_{animeId}", out _);
        }

        return outcome;
    }

    private async Task<int?> FindUserRateIdAsync(int userId, int targetId, string targetType, CancellationToken ct)
    {
        try
        {
            var url = $"v2/user_rates?user_id={userId}&target_id={targetId}&target_type={targetType}";
            using var response = await GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, default, ct);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var rateId))
                    {
                        return rateId;
                    }
                }
            }
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warning(ex, "ShikiApiService ({Tracker}): FindUserRateIdAsync failed for target {TargetId} ({TargetType})", Name, targetId, targetType);
            return null;
        }
    }

    public async Task<Dictionary<int, string>> FetchPostersFromGraphQlAsync(IEnumerable<int> ids, bool isManga, CancellationToken ct = default)
    {
        var posterMap = new Dictionary<int, string>();
        var idList = ids.Where(id => id > 0).Distinct().ToList();
        if (idList.Count == 0) return posterMap;

        string entityType = isManga ? "mangas" : "animes";
        var neededIds = new List<int>();

        // 1. Check in-memory fast cache
        foreach (var id in idList)
        {
            var memKey = $"{entityType}_{id}";
            if (_posterMemoryCache.TryGetValue(memKey, out var memUrl) && !string.IsNullOrEmpty(memUrl))
            {
                posterMap[id] = memUrl;
            }
            else
            {
                neededIds.Add(id);
            }
        }

        if (neededIds.Count == 0) return posterMap;

        // 2. Check SQLite persistent cache (http_response_cache table)
        var toQueryFromNetwork = new List<int>();
        foreach (var id in neededIds)
        {
            if (ct.IsCancellationRequested) break;
            var dbKey = $"shiki_poster_{entityType}_{id}";
            var cachedBytes = await _httpCache.GetCachedBodyAsync(dbKey, ct);
            if (cachedBytes != null && cachedBytes.Length > 0)
            {
                var cachedUrl = Encoding.UTF8.GetString(cachedBytes);
                if (!string.IsNullOrEmpty(cachedUrl) && !AnimeEntity.IsMissingPosterUrl(cachedUrl))
                {
                    _posterMemoryCache[$"{entityType}_{id}"] = cachedUrl;
                    posterMap[id] = cachedUrl;
                    continue;
                }
            }
            toQueryFromNetwork.Add(id);
        }

        if (toQueryFromNetwork.Count == 0) return posterMap;

        // 3. Query network via GraphQL only for truly missing posters
        foreach (var chunk in toQueryFromNetwork.Chunk(50))
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var idsStr = string.Join(",", chunk);
                var query = $"{{\"query\":\"{{ {entityType}(ids: \\\"{idsStr}\\\", limit: 50) {{ id poster {{ originalUrl }} }} }}\"}}";
                using var request = new HttpRequestMessage(HttpMethod.Post, ShikiBaseUrl + "graphql")
                {
                    Content = new StringContent(query, Encoding.UTF8, "application/json")
                };

                using var response = await SendRequestAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("ShikiApiService ({Tracker}): GraphQL poster fetch failed with HTTP {Status}", Name, response.StatusCode);
                    continue;
                }

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, default, ct);
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty(entityType, out var itemsArray) &&
                    itemsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in itemsArray.EnumerateArray())
                    {
                        if (!entry.TryGetProperty("id", out var idProp)) continue;
                        var idStr = idProp.GetString();
                        if (!int.TryParse(idStr, out var id)) continue;

                        if (entry.TryGetProperty("poster", out var poster) &&
                            poster.ValueKind == JsonValueKind.Object &&
                            poster.TryGetProperty("originalUrl", out var origProp) &&
                            origProp.ValueKind == JsonValueKind.String)
                        {
                            var url = origProp.GetString();
                            if (!string.IsNullOrEmpty(url) && !AnimeEntity.IsMissingPosterUrl(url))
                            {
                                if (!Uri.TryCreate(url, UriKind.Absolute, out var absUri) ||
                                    (absUri.Scheme != Uri.UriSchemeHttp && absUri.Scheme != Uri.UriSchemeHttps))
                                {
                                    var baseUri = new Uri(ShikiEndpoints.BaseUrl(ShikiMirror.One));
                                    url = $"{baseUri.Scheme}{Uri.SchemeDelimiter}{baseUri.Authority}" + (url.StartsWith('/') ? url : "/" + url);
                                }
                                posterMap[id] = url;
                                _posterMemoryCache[$"{entityType}_{id}"] = url;
                                _ = _httpCache.SetCachedBodyAsync($"shiki_poster_{entityType}_{id}", Encoding.UTF8.GetBytes(url), CancellationToken.None);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Warning(ex, "ShikiApiService ({Tracker}): Exception during GraphQL poster fetch chunk", Name);
            }
        }

        return posterMap;
    }

    public async Task EnrichMissingPostersFromGraphQlAsync(List<AnimeEntity> list, bool isManga, CancellationToken ct)
    {
        var missing = list
            .Where(x => string.IsNullOrEmpty(x.MainPictureUrl) || AnimeEntity.IsMissingPosterUrl(x.MainPictureUrl))
            .ToList();

        if (missing.Count == 0) return;

        Log.Information("ShikiApiService ({Tracker}): Found {Count} items missing poster. Enriching via Shikimori GraphQL...", Name, missing.Count);

        var shikiIdMap = new Dictionary<int, List<AnimeEntity>>();
        foreach (var item in missing)
        {
            var shikiId = ResolveShikiTargetId(item.Id);
            if (shikiId <= 0) continue;
            if (!shikiIdMap.TryGetValue(shikiId, out var group))
            {
                group = new List<AnimeEntity>();
                shikiIdMap[shikiId] = group;
            }
            group.Add(item);
        }

        var posterMap = await FetchPostersFromGraphQlAsync(shikiIdMap.Keys, isManga, ct);
        int enrichedCount = 0;

        foreach (var (shikiId, url) in posterMap)
        {
            if (shikiIdMap.TryGetValue(shikiId, out var items))
            {
                foreach (var item in items)
                {
                    item.MainPictureUrl = url;
                    enrichedCount++;
                }
            }
        }

        Log.Information("ShikiApiService ({Tracker}): Successfully enriched {EnrichedCount}/{TotalMissing} posters via Shikimori GraphQL",
            Name, enrichedCount, missing.Count);
    }
}
