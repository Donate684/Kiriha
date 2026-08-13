using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Infrastructure.Http;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Tracking.Auth;
using Kiriha.Core.Tracking.Sync;

using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

public partial class MalApiService : IMalApiService, IDisposable
{
    // Resolved once from Constants â€” no parallel const that can drift from the URL
    // wired into the IHttpClientFactory "MalClient" registration.
    private static readonly string MalBaseUrl = AppConstants.Api.Mal.BaseUrl;

    private static readonly string ListStatusFields = "num_episodes_watched,score,status,num_times_rewatched,is_rewatching,notes,start_date,finish_date";
    private static readonly string AnimeFields = $"list_status{{{ListStatusFields}}},my_list_status{{{ListStatusFields}}},main_picture,synopsis,mean,rank,popularity,num_episodes,start_season,genres,studios,alternative_titles,status,start_date,nsfw,rating,media_type,broadcast,external_links";

    private static readonly string MangaListStatusFields = "num_chapters_read,num_volumes_read,score,status,num_times_reread,is_rereading,notes,start_date,finish_date";
    private static readonly string MangaFields = $"list_status{{{MangaListStatusFields}}},my_list_status{{{MangaListStatusFields}}},main_picture,synopsis,mean,rank,popularity,num_chapters,num_volumes,authors,genres,alternative_titles,status,start_date,nsfw,media_type,external_links";

    private readonly HttpClient _httpClient;
    private readonly Kiriha.Core.Abstractions.Services.ISettingsService _settingsService;
    private readonly MalTokenManager _tokenManager;
    private readonly JikanApiService _jikanApi;
    private readonly HttpConditionalCache _httpCache;
    // Outbound throttle: ~3.3 req/s (one token every 300 ms). MAL doesn't publish a
    // hard rate-limit but Cloudflare in front of api.myanimelist.net bites at ~5 req/s
    // sustained; 300 ms keeps us comfortably below that and avoids 429 storms when
    // SyncManager flushes a backlog.
    private readonly RateLimiter _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 1,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 100,
        ReplenishmentPeriod = TimeSpan.FromMilliseconds(300),
        TokensPerPeriod = 1,
        AutoReplenishment = true,
    });

    public string Name => "MyAnimeList";
    public bool IsEnabled => _settingsService.Current.Api.Mal != null;

    public MalApiService(HttpClient httpClient, Kiriha.Core.Abstractions.Services.ISettingsService settingsService, MalTokenManager tokenManager, JikanApiService jikanApi, IHttpCacheRepository httpCacheRepo)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _tokenManager = tokenManager;
        _jikanApi = jikanApi;
        _httpCache = new HttpConditionalCache(httpClient, httpCacheRepo, "MalApi");
    }

    public void Dispose()
    {
        _rateLimiter.Dispose();
    }
}
