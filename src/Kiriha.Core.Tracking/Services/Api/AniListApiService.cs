using Kiriha.Core.Abstractions.Services;
using System;
using Kiriha.Core.Shared;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Kiriha.Infrastructure;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Abstractions.Repositories;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

// Moved to Kiriha.Core.Domain.Models.AniListAiringInfo

public class AniListApiService : IDisposable, IAniListApiService
{
    private const string Endpoint = Kiriha.Core.Domain.Constants.AppConstants.Api.AniList.BaseUrl;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan EmptyTtl = TimeSpan.FromHours(12);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IHttpCacheRepository _cache;
    private readonly RateLimiter _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 1,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 100,
        ReplenishmentPeriod = TimeSpan.FromMilliseconds(2200),
        TokensPerPeriod = 1,
        AutoReplenishment = true,
    });

    public AniListApiService(HttpClient httpClient, IHttpCacheRepository cache)
    {
        _httpClient = httpClient;
        _cache = cache;
    }

    public async Task<AniListAiringInfo?> GetNextAiringAsync(int malId, bool force = false, CancellationToken ct = default)
    {
        if (malId <= 0) return null;

        var cacheKey = CacheKey(malId);
        if (!force)
        {
            var cached = await TryReadCacheAsync(cacheKey);
            if (cached.Fresh) return cached.Value;
        }

        using var lease = await _rateLimiter.AcquireAsync(1, ct);

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
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("User-Agent", AppInfo.UserAgent);

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("AniList: request for MAL {MalId} returned {Status}", malId, response.StatusCode);
                return (await TryReadCacheAsync(cacheKey, allowStale: true)).Value;
            }

            var contentString = await response.Content.ReadAsStringAsync(ct);

            using var json = JsonDocument.Parse(contentString);
            try
            {
                var result = AniListParser.ParseAiringInfo(json.RootElement, malId);
                await WriteCacheAsync(cacheKey, result);
                return result;
            }
            catch (Exception parseEx)
            {
                Log.Warning(parseEx, "AniList: failed to parse airing response for MAL {MalId}", malId);
                return (await TryReadCacheAsync(cacheKey, allowStale: true)).Value;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warning(ex, "AniList: failed to fetch next airing for MAL {MalId}", malId);
            return (await TryReadCacheAsync(cacheKey, allowStale: true)).Value;
        }
    }

    private async Task<(bool Fresh, AniListAiringInfo? Value)> TryReadCacheAsync(string cacheKey, bool allowStale = false)
    {
        try
        {
            var entry = await _cache.GetAsync(cacheKey);
            if (entry == null || entry.Body.Length == 0) return (false, null);

            var cached = JsonSerializer.Deserialize<AniListAiringCacheEntry>(entry.Body, JsonOptions);
            if (cached == null) return (false, null);

            var age = DateTime.UtcNow - entry.CreatedAt;
            var ttl = cached.Value == null ? EmptyTtl : DefaultTtl;
            if (!allowStale && age > ttl) return (false, null);
            return (true, cached.Value);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "AniList: cache lookup failed");
            return (false, null);
        }
    }

    private async Task WriteCacheAsync(string cacheKey, AniListAiringInfo? value)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new AniListAiringCacheEntry(value), JsonOptions);
            await _cache.UpsertAsync(cacheKey, etag: null, lastModified: null, bytes);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "AniList: failed to persist airing cache");
        }
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
    private sealed record AniListAiringCacheEntry(AniListAiringInfo? Value);

    public void Dispose()
    {
        _rateLimiter.Dispose();
    }
}
