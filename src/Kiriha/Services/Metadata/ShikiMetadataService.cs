using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Shared;
using Kiriha.Core.Tracking.Api;
using Kiriha.Infrastructure.Http;
using Serilog;

namespace Kiriha.Services.Data.Metadata;

public partial class ShikiMetadataService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IMetadataRepository _metadataRepo;
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly ISettingsService _settingsService;
    private readonly HttpConditionalCache _httpCache;
    private readonly ShikiHostResolver _hostResolver;
    private readonly IUiDispatcher _uiDispatcher;

    private readonly ShikiRateLimiter _rateLimiter;
    private readonly SemaphoreSlim _concurrentFetches = new(2, 2);
    private readonly ConcurrentDictionary<int, byte> _activeFetches = new();

    protected ShikiMetadataService()
    {
        _httpClient = null!;
        _settingsService = null!;
        _metadataRepo = null!;
        _userAnimeRepo = null!;
        _hostResolver = null!;
        _uiDispatcher = null!;
        _rateLimiter = null!;
        _httpCache = null!;
    }

    public ShikiMetadataService(
        IHttpClientFactory httpClientFactory,
        ISettingsService settingsService,
        IMetadataRepository metadataRepo,
        IUserAnimeRepository userAnimeRepo,
        IHttpCacheRepository httpCacheRepo,
        ShikiHostResolver hostResolver,
        IUiDispatcher uiDispatcher,
        ShikiRateLimiter rateLimiter)
    {
        _httpClient = httpClientFactory.CreateClient("ShikiClient");
        _settingsService = settingsService;
        _metadataRepo = metadataRepo;
        _userAnimeRepo = userAnimeRepo;
        _hostResolver = hostResolver;
        _uiDispatcher = uiDispatcher;
        _rateLimiter = rateLimiter;
        _httpCache = new HttpConditionalCache(
            _httpClient,
            httpCacheRepo,
            "ShikiMeta",
            (client, request, innerCt) => ShikiHttp.SendShikiAsync(client, request, _hostResolver, innerCt));
    }

    // Resolved per-call so a mid-session mirror switch is honoured immediately.
    // "ShikiClient" has no HttpClient.BaseAddress, so we must always send absolute URLs.
    private string ShikiBaseUrl => ShikiEndpoints.BaseUrl(_settingsService.Current.Api.ShikiMirror);

    private static int GetCacheId(int malId, MediaKind mediaKind) =>
        mediaKind switch
        {
            MediaKind.Manga => malId | 0x40000000,
            MediaKind.LightNovel => malId | 0x20000000,
            _ => malId
        };

    /// <summary>
    /// Returns Shikimori metadata for <paramref name="animeId"/>, fetching from
    /// the API on miss. <paramref name="maxAge"/> bounds the cache freshness:
    /// when the persisted entry is older, we re-fetch (the conditional GET via
    /// <see cref="HttpConditionalCache"/> makes this cheap — usually 304).
    /// Pass <c>null</c> to accept any age (default for completed shows).
    ///
    /// <paramref name="onFetched"/> is invoked on every successful return —
    /// cache hit or fresh fetch — so periodic syncs (e.g. AiringInfoService's
    /// Shiki fallback) keep applying current values to the UI.
    /// </summary>
    public virtual async Task<ShikiMetadata?> GetOrFetchMetadataAsync(int animeId, TimeSpan? maxAge = null, Func<ShikiMetadata, Task>? onFetched = null, MediaKind mediaKind = MediaKind.Anime)
    {
        int cacheId = GetCacheId(animeId, mediaKind);
        var cached = await _metadataRepo.GetAsync(cacheId);
        // When a TTL is requested, treat both genuinely-old entries and pre-TTL
        // legacy rows (FetchedAt == default after the schema migration) as stale —
        // otherwise a user's existing metadata would skip the airing refresh
        // forever. The first successful upsert stamps a real timestamp and
        // normal TTL semantics take over.
        bool stale = cached != null
                     && maxAge.HasValue
                     && (cached.FetchedAt == default
                         || DateTime.UtcNow - cached.FetchedAt > maxAge.Value);

        if (cached != null && !stale)
        {
            if (string.IsNullOrEmpty(cached.PosterUrl) && _settingsService.Current.Api.ShikiMirror == ShikiMirror.One)
            {
                var poster = await FetchPosterFromGraphQlAsync(animeId, mediaKind, CancellationToken.None);
                if (!string.IsNullOrEmpty(poster))
                {
                    cached.PosterUrl = poster;
                    await _metadataRepo.UpsertAsync(cached);
                }
            }

            if (onFetched != null) await onFetched(cached);
            return cached;
        }

        if (!_activeFetches.TryAdd(cacheId, 0))
        {
            // Another fetch is already in flight; serve whatever we have
            // (possibly stale) rather than spinning a duplicate request.
            return cached;
        }

        try
        {
            var fetched = await FetchMetadataFromApiAsync(animeId, CancellationToken.None, mediaKind);
            if (fetched != null)
            {
                await _metadataRepo.UpsertAsync(fetched);
                if (onFetched != null) await onFetched(fetched);
                return fetched;
            }

            // Live fetch failed but we still have a stale entry � better to
            // return it than nothing, the caller can apply best-effort.
            if (cached != null)
            {
                if (onFetched != null) await onFetched(cached);
                return cached;
            }
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Background check for metadata failed for {Id}", animeId);
            return cached;
        }
        finally
        {
            _activeFetches.TryRemove(cacheId, out _);
        }
    }

    private async Task<ShikiMetadata?> FetchMetadataFromApiAsync(int animeId, CancellationToken ct = default, MediaKind mediaKind = MediaKind.Anime)
    {
        // Conditional GET via http_response_cache: on a 304 we skip JSON parse
        // entirely (the helper replays the persisted body, which we then parse).
        // ResilientHttpHandler still handles 429s with backoff, so the manual
        // retry loop the original code carried is no longer needed.
        try
        {
            var result = await _httpCache.SendForResultAsync(
                requestFactory: innerCt =>
                {
                    string endpoint = mediaKind == MediaKind.Anime ? "animes" : "mangas";
                    var request = new HttpRequestMessage(HttpMethod.Get, $"{ShikiBaseUrl}{endpoint}/{animeId}");
                    request.Headers.Add("User-Agent", AppInfo.UserAgent);
                    return Task.FromResult(request);
                },
                throttle: ct => _rateLimiter.ThrottleAsync(ct),
                ct: ct);

            // 404: anime not on Shikimori. Persist a sentinel so we don't
            // re-attempt on every sync tick (matches the original behaviour
            // of the manual retry loop).
            if (result.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new ShikiMetadata { Id = GetCacheId(animeId, mediaKind), Russian = "", Description = "" };
            }

            if (result.Body is null) return null; // transient failure — retry next tick

            var metadata = System.Text.Json.JsonSerializer.Deserialize<ShikiMetadata>(result.Body);
            if (metadata != null)
            {
                metadata.Id = GetCacheId(animeId, mediaKind);

                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(result.Body);
                    if (doc.RootElement.TryGetProperty("image", out var imgNode) && imgNode.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        var orig = imgNode.TryGetProperty("original", out var oProp) && oProp.ValueKind == System.Text.Json.JsonValueKind.String ? oProp.GetString() : null;
                        var prev = imgNode.TryGetProperty("preview", out var pProp) && pProp.ValueKind == System.Text.Json.JsonValueKind.String ? pProp.GetString() : null;
                        var candidate = orig ?? prev;
                        if (!string.IsNullOrEmpty(candidate) && !AnimeEntity.IsMissingPosterUrl(candidate))
                        {
                            var baseUri = new Uri(ShikiBaseUrl);
                            var websiteRoot = $"{baseUri.Scheme}://{baseUri.Authority}";
                            metadata.PosterUrl = candidate.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                                ? candidate
                                : $"{websiteRoot}{(candidate.StartsWith('/') ? candidate : "/" + candidate)}";
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to parse image from Shikimori metadata for {Id}", animeId);
                }

                if (string.IsNullOrEmpty(metadata.PosterUrl) && _settingsService.Current.Api.ShikiMirror == ShikiMirror.One)
                {
                    metadata.PosterUrl = await FetchPosterFromGraphQlAsync(animeId, mediaKind, ct);
                }
            }
            return metadata;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception fetching Shikimori metadata for {Id}", animeId);
            return null;
        }
    }

    public virtual async Task<string?> FetchPosterFromGraphQlAsync(int animeId, MediaKind mediaKind, CancellationToken ct)
    {
        string entityType = mediaKind == MediaKind.Manga ? "mangas" : "animes";
        var dbKey = $"shiki_poster_{entityType}_{animeId}";

        var cachedBytes = await _httpCache.GetCachedBodyAsync(dbKey, ct);
        if (cachedBytes != null && cachedBytes.Length > 0)
        {
            var cachedUrl = Encoding.UTF8.GetString(cachedBytes);
            if (!string.IsNullOrEmpty(cachedUrl) && !AnimeEntity.IsMissingPosterUrl(cachedUrl))
            {
                return cachedUrl;
            }
        }

        try
        {
            await _rateLimiter.ThrottleAsync(ct);

            var query = $"{{\"query\":\"{{ {entityType}(ids: \\\"{animeId}\\\", limit: 1) {{ id poster {{ originalUrl }} }} }}\"}}";
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ShikiBaseUrl}graphql")
            {
                Content = new StringContent(query, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("User-Agent", AppInfo.UserAgent);

            using var response = await ShikiHttp.SendShikiAsync(_httpClient, request, _hostResolver, ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty(entityType, out var list) &&
                list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (item.TryGetProperty("poster", out var poster) &&
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
                            _ = _httpCache.SetCachedBodyAsync(dbKey, Encoding.UTF8.GetBytes(url), CancellationToken.None);
                            return url;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to fetch poster from Shikimori GraphQL for {Id}", animeId);
        }
        return null;
    }

    public void Dispose()
    {
        _concurrentFetches.Dispose();
    }
}



