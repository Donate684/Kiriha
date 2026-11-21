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
using Kiriha.Core.Domain.Utils.Async;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Sync;
using Serilog;

namespace Kiriha.Core.Tracking.Metadata;

public partial class ShikiMetadataService : IShikiMetadataService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IMetadataRepository _metadataRepo;
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly ISettingsService _settingsService;
    private readonly HttpConditionalCache _httpCache;
    private readonly ShikiHostResolver _hostResolver;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IAnimeRepository? _library;
    private readonly LibrarySyncGate? _syncGate;

    private readonly ShikiRateLimiter _rateLimiter;
    private readonly ShikiStateCache? _stateCache;
    private readonly SemaphoreSlim _concurrentFetches = new(2, 2);
    private readonly ConcurrentDictionary<MediaKey, byte> _activeFetches = new();
    private readonly SharedAsyncWork<(MediaKey Key, TimeSpan? MaxAge), ShikiMetadata?> _metadataRefreshes = new();

    public ShikiMetadataService(
        IHttpClientFactory httpClientFactory,
        ISettingsService settingsService,
        IMetadataRepository metadataRepo,
        IUserAnimeRepository userAnimeRepo,
        IHttpCacheRepository httpCacheRepo,
        ShikiHostResolver hostResolver,
        IUiDispatcher uiDispatcher,
        ShikiRateLimiter rateLimiter,
        ShikiStateCache? stateCache = null,
        IAnimeRepository? library = null,
        LibrarySyncGate? syncGate = null,
        TimeProvider? clock = null)
    {
        _httpClient = httpClientFactory.CreateClient("ShikiClient");
        _settingsService = settingsService;
        _metadataRepo = metadataRepo;
        _userAnimeRepo = userAnimeRepo;
        _hostResolver = hostResolver;
        _uiDispatcher = uiDispatcher;
        _rateLimiter = rateLimiter;
        _stateCache = stateCache;
        _library = library;
        _syncGate = syncGate;
        _httpCache = new HttpConditionalCache(
            _httpClient,
            httpCacheRepo,
            "ShikiMeta",
            (client, request, innerCt) => ShikiHttp.SendShikiAsync(client, request, _hostResolver, innerCt),
            clock);
    }

    // Resolved per-call so a mid-session mirror switch is honoured immediately.
    // "ShikiClient" has no HttpClient.BaseAddress, so we must always send absolute URLs.
    private string ShikiBaseUrl => ShikiEndpoints.BaseUrl(_settingsService.Current.Api.ShikiMirror);

    public async Task<ShikiMetadata?> GetOrFetchMetadataAsync(int animeId, TimeSpan? maxAge = null, Func<ShikiMetadata, Task>? onFetched = null, MediaKind mediaKind = MediaKind.Anime)
    {
        var cacheKey = new MediaKey(animeId, mediaKind);
        var result = await _metadataRefreshes.RunAsync((cacheKey, maxAge), () => ReadOrRefreshMetadataAsync(animeId, mediaKind, maxAge));
        if (result is not null && onFetched is not null) await onFetched(result);
        return result;
    }

    private async Task<ShikiMetadata?> ReadOrRefreshMetadataAsync(int animeId, MediaKind mediaKind, TimeSpan? maxAge)
    {
        var cached = await _metadataRepo.GetAsync(animeId, mediaKind);
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
            if (cached.PosterUrl is null && _settingsService.Current.Api.ShikiMirror == ShikiMirror.One)
            {
                var poster = await FetchPosterFromGraphQlAsync(animeId, mediaKind, CancellationToken.None);
                cached.PosterUrl = poster ?? string.Empty;
                await _metadataRepo.UpsertAsync(cached);
            }

            return cached;
        }

        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var fetched = await FetchMetadataFromApiAsync(animeId, deadline.Token, mediaKind, maxAge);
            if (fetched != null)
            {
                await _metadataRepo.UpsertAsync(fetched, deadline.Token);
                return fetched;
            }
            return cached;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Background check for metadata failed for {Id}", animeId);
            return cached;
        }
    }

    private async Task<ShikiMetadata?> FetchMetadataFromApiAsync(int animeId, CancellationToken ct, MediaKind mediaKind, TimeSpan? maxAge)
    {
        // The raw Shikimori response is shared with the airing endpoint.
        // A transient failure must not extend the derived metadata lifetime.
        try
        {
            string endpoint = mediaKind == MediaKind.Anime ? "animes" : "mangas";
            var url = $"{ShikiBaseUrl}{endpoint}/{animeId}";
            var result = await _httpCache.SendForResultAsync(
                url,
                requestFactory: _ =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("User-Agent", AppInfo.UserAgent);
                    return Task.FromResult(request);
                },
                throttle: ct => _rateLimiter.ThrottleAsync(ct),
                ct: ct,
                localTtl: maxAge ?? TimeSpan.FromHours(6));

            // 404: anime not on Shikimori. Persist a sentinel so we don't
            // re-attempt on every sync tick (matches the original behaviour
            // of the manual retry loop).
            if (result.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new ShikiMetadata { Id = animeId, MediaKind = mediaKind, Russian = "", Description = "", PosterUrl = "" };
            }

            if (result.Body is null || result.IsStaleFallback) return null;

            var metadata = System.Text.Json.JsonSerializer.Deserialize<ShikiMetadata>(result.Body);
            if (metadata != null)
            {
                metadata.Id = animeId;
                metadata.MediaKind = mediaKind;

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

                    if (doc.RootElement.TryGetProperty("studios", out var studiosNode) && studiosNode.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var studios = new List<string>();
                        foreach (var s in studiosNode.EnumerateArray())
                        {
                            var sName = s.TryGetProperty("name", out var sn) && sn.ValueKind == System.Text.Json.JsonValueKind.String ? sn.GetString() : null;
                            if (!string.IsNullOrWhiteSpace(sName)) studios.Add(sName);
                        }
                        if (studios.Count > 0) metadata.Studios = studios;
                    }

                    if (doc.RootElement.TryGetProperty("genres", out var genresNode) && genresNode.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var genres = new List<string>();
                        foreach (var g in genresNode.EnumerateArray())
                        {
                            var gName = g.TryGetProperty("name", out var gn) && gn.ValueKind == System.Text.Json.JsonValueKind.String ? gn.GetString() : null;
                            if (!string.IsNullOrWhiteSpace(gName)) genres.Add(gName);
                        }
                        if (genres.Count > 0) metadata.Genres = genres;
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

                metadata.PosterUrl ??= string.Empty;
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

    public async Task<string?> FetchPosterFromGraphQlAsync(int animeId, MediaKind mediaKind, CancellationToken ct)
    {
        var map = await ShikiGraphQlPosterFetcher.FetchPostersAsync(
            [animeId],
            isManga: mediaKind != MediaKind.Anime,
            ShikiBaseUrl,
            _httpCache,
            _stateCache?.PosterMemoryCache,
            async (req, innerCt) =>
            {
                await _rateLimiter.ThrottleAsync(innerCt);
                req.Headers.Add("User-Agent", AppInfo.UserAgent);
                return await ShikiHttp.SendShikiAsync(_httpClient, req, _hostResolver, innerCt);
            },
            ct);

        return map.TryGetValue(animeId, out var url) ? url : null;
    }

    public void Dispose()
    {
        _concurrentFetches.Dispose();
    }
}



