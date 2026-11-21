using Kiriha.Core.Tracking.Auth;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Utils.Async;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

public partial class AniListApiService : IDisposable, IAniListApiService
{
    private const string Endpoint = Kiriha.Core.Domain.Constants.AppConstants.Api.AniList.BaseUrl;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan EmptyTtl = TimeSpan.FromHours(12);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IHttpCacheRepository _cache;
    private readonly ISettingsService _settingsService;
    private readonly TimeProvider _clock;
    private readonly IAniListMediaMapRepository? _mediaMapRepo;
    private readonly ConcurrentDictionary<(int MalId, string MediaType), int> _malToAniListMap = new();
    private readonly SharedAsyncWork<(int MalId, bool Force), EpisodeAiringInfo?> _airingRefreshes = new();

    private readonly RateLimiter _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 1,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 100,
        ReplenishmentPeriod = TimeSpan.FromMilliseconds(2200),
        TokensPerPeriod = 1,
        AutoReplenishment = true,
    });

    public string Name => "AniList";
    public string TrackerId => TrackerConstants.Ids.AniList;
    public bool IsEnabled
    {
        get
        {
            var acc = TrackerRequestAccount.Get(_settingsService, TrackerConstants.Ids.AniList);
            return acc != null && acc.IsEnabled && acc.Tokens != null;
        }
    }

    public AniListApiService(
        HttpClient httpClient,
        IHttpCacheRepository cache,
        ISettingsService settingsService,
        IAniListMediaMapRepository? mediaMapRepo = null,
        TimeProvider? clock = null)
    {
        _httpClient = httpClient;
        _cache = cache;
        _settingsService = settingsService;
        _mediaMapRepo = mediaMapRepo;
        _clock = clock ?? TimeProvider.System;
    }

    private async Task<JsonDocument?> ExecuteGraphQlAsync(string query, object? variables = null, CancellationToken ct = default)
    {
        using var lease = await _rateLimiter.AcquireAsync(1, ct);

        var payload = new
        {
            query,
            variables
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("User-Agent", AppInfo.UserAgent);

        var token = TrackerRequestAccount.Get(_settingsService, TrackerConstants.Ids.AniList)?.Tokens?.AccessToken;
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound && query.Contains("MediaListCollection"))
            {
                // AniList GraphQL returns HTTP 404 Not Found when a user has no lists/entries in their MediaListCollection.
                // Synthesize an empty collection payload so the caller receives an empty list rather than a sync failure.
                return JsonDocument.Parse("""{"data":{"MediaListCollection":{"lists":[]}}}""");
            }

            Log.Warning("AniList: GraphQL request returned {StatusCode}", response.StatusCode);
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(ct);
        var doc = await JsonDocument.ParseAsync(stream, default, ct);
        if (doc.RootElement.TryGetProperty("errors", out var errors) &&
            (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null))
        {
            Log.Warning("AniList: GraphQL returned error payload: {Errors}", errors.ToString());
            return null;
        }
        return doc;
    }

    internal async Task CacheMediaMappingAsync(int primaryId, string mediaType, int aniListId, bool isAniListNativeId)
    {
        _malToAniListMap[(primaryId, mediaType)] = aniListId;
        if (!isAniListNativeId || primaryId <= 0 || aniListId <= 0 || _mediaMapRepo is null)
            return;

        try
        {
            await _mediaMapRepo.UpsertAsync(primaryId, mediaType, aniListId);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "AniList: failed to persist native media ID mapping for {Id}", primaryId);
        }
    }

    internal async Task<(int? MediaId, SyncOutcome Outcome)> ResolveAniListMediaIdAsync(
        int malId,
        string mediaType = "ANIME",
        CancellationToken ct = default)
    {
        if (_malToAniListMap.TryGetValue((malId, mediaType), out var cachedId))
            return (cachedId, SyncOutcome.Success);

        var persistedNativeId = await TryReadMappedMediaIdAsync(malId, mediaType);
        if (persistedNativeId.HasValue)
        {
            _malToAniListMap[(malId, mediaType)] = persistedNativeId.Value;
            return (persistedNativeId.Value, SyncOutcome.Success);
        }

        const string query = """
        query ($malId: Int, $type: MediaType) {
          Media(idMal: $malId, type: $type) {
            id
            idMal
          }
        }
        """;

        using var lease = await _rateLimiter.AcquireAsync(1, ct);

        var payload = new { query, variables = new { malId, type = mediaType } };
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("User-Agent", AppInfo.UserAgent);

        var token = TrackerRequestAccount.Get(_settingsService, TrackerConstants.Ids.AniList)?.Tokens?.AccessToken;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var nativeFallback = await TryResolveNativeAniListIdFallbackAsync(malId, mediaType, token, ct);
                if (nativeFallback.HasValue)
                {
                    await CacheMediaMappingAsync(malId, mediaType, nativeFallback.Value, isAniListNativeId: true);
                    return (nativeFallback.Value, SyncOutcome.Success);
                }

                Log.Warning("AniList: Media not found for MAL {MalId} (404)", malId);
                return (null, SyncOutcome.PermanentFailure);
            }

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("AniList: GraphQL returned {StatusCode} for MAL {MalId}", response.StatusCode, malId);
                return (null, SyncOutcome.TransientFailure);
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, default, ct);

            if (doc != null)
            {
                if (doc.RootElement.TryGetProperty("errors", out _) &&
                    (!doc.RootElement.TryGetProperty("data", out var d) || d.ValueKind == JsonValueKind.Null))
                {
                    Log.Warning("AniList: GraphQL error resolving media for MAL {MalId}", malId);
                    return (null, SyncOutcome.PermanentFailure);
                }

                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("Media", out var media) &&
                    media.ValueKind == JsonValueKind.Object &&
                    media.TryGetProperty("id", out var idProp) &&
                    idProp.TryGetInt32(out var aniListId))
                {
                    _malToAniListMap[(malId, mediaType)] = aniListId;
                    return (aniListId, SyncOutcome.Success);
                }
            }

            return (null, SyncOutcome.PermanentFailure);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AniList: Network error resolving mediaId for MAL {MalId}", malId);
            return (null, SyncOutcome.TransientFailure);
        }
    }

    public Task<EpisodeAiringInfo?> GetNextAiringAsync(int malId, bool force = false, CancellationToken ct = default) =>
        malId <= 0 ? Task.FromResult<EpisodeAiringInfo?>(null) :
            _airingRefreshes.RunAsync((malId, force), () => RefreshAiringAsync(malId, force), ct);

    private async Task<EpisodeAiringInfo?> RefreshAiringAsync(int malId, bool force)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = deadline.Token;
        var cacheKey = CacheKey(malId);
        var cached = await TryReadCacheAsync(cacheKey, ct);
        if (!force && cached.Fresh) return cached.Value;
        var fallback = cached.Value is { } prior ? prior with { IsStaleFallback = true } : null;

        var payload = new AniListGraphQlRequest(
            Query: """
                   query ($malId: Int) {
                     Media(idMal: $malId, type: ANIME) {
                       id
                       idMal
                       status
                       episodes
                       nextAiringEpisode {
                         episode
                         airingAt
                       }
                     }
                   }
                   """,
            Variables: new AniListVariables(malId));

        try
        {
            using var lease = await _rateLimiter.AcquireAsync(1, ct);
            if (!lease.IsAcquired) return fallback;

            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("User-Agent", AppInfo.UserAgent);

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("AniList: request for MAL {MalId} returned {Status}", malId, response.StatusCode);
                return fallback;
            }

            var contentString = await response.Content.ReadAsStringAsync(ct);

            using var json = JsonDocument.Parse(contentString);
            var root = json.RootElement;
            if ((root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0) ||
                !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("Media", out var media) || media.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object))
                return fallback;

            var result = AniListParser.ParseAiringInfo(root, malId);
            await WriteCacheAsync(cacheKey, result, ct);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AniList: failed to fetch next airing for MAL {MalId}", malId);
            return fallback;
        }
    }

    private async Task<(bool Fresh, EpisodeAiringInfo? Value)> TryReadCacheAsync(string cacheKey, CancellationToken ct)
    {
        try
        {
            var entry = await _cache.GetAsync(cacheKey, ct);
            if (entry is null || entry.Body.Length == 0) return (false, null);

            var cached = JsonSerializer.Deserialize<AniListAiringCacheEntry>(entry.Body, JsonOptions);
            if (cached is null) return (false, null);

            var age = _clock.GetUtcNow().UtcDateTime - entry.CreatedAt;
            var ttl = cached.Value is null ? EmptyTtl : DefaultTtl;
            return (age <= ttl, cached.Value);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "AniList: cache lookup failed");
            return (false, null);
        }
    }

    private async Task WriteCacheAsync(string cacheKey, EpisodeAiringInfo? value, CancellationToken ct)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new AniListAiringCacheEntry(value), JsonOptions);
            await _cache.UpsertAsync(cacheKey, etag: null, lastModified: null, bytes, ct);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "AniList: failed to persist airing cache");
        }
    }



    private async Task<int?> TryReadMappedMediaIdAsync(int primaryId, string mediaType)
    {
        if (_mediaMapRepo is null)
            return null;

        try
        {
            var mapped = await _mediaMapRepo.GetAniListIdAsync(primaryId, mediaType);
            return mapped is > 0 ? mapped : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<int?> TryResolveNativeAniListIdFallbackAsync(int candidateAniListId, string mediaType, string? token, CancellationToken ct)
    {
        const string nativeQuery = """
        query ($id: Int, $type: MediaType) {
          Media(id: $id, type: $type) {
            id
            idMal
          }
        }
        """;

        using var lease = await _rateLimiter.AcquireAsync(1, ct);
        var payload = new { query = nativeQuery, variables = new { id = candidateAniListId, type = mediaType } };
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("User-Agent", AppInfo.UserAgent);
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, default, ct);
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("Media", out var media) &&
            media.ValueKind == JsonValueKind.Object &&
            media.TryGetProperty("id", out var idProp) &&
            idProp.TryGetInt32(out var resolvedId))
        {
            bool hasMalId = media.TryGetProperty("idMal", out var malProp)
                            && malProp.ValueKind == JsonValueKind.Number
                            && malProp.TryGetInt32(out var existingMal)
                            && existingMal > 0;
            if (!hasMalId)
                return resolvedId;
        }

        return null;
    }

    private static string CacheKey(int malId)
    {
        var raw = $"AniList:nextAiring:{malId}";
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(raw), hash);
        return Convert.ToHexString(hash);
    }

    private sealed record AniListGraphQlRequest(string Query, AniListVariables Variables);
    private sealed record AniListVariables(int MalId);
    private sealed record AniListAiringCacheEntry(EpisodeAiringInfo? Value);

    public void Dispose()
    {
        _rateLimiter.Dispose();
    }
}
