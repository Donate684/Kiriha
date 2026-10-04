using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Shared;
using Kiriha.Core.Tracking.Auth;
using Kiriha.Infrastructure.Http;
using Serilog;


namespace Kiriha.Core.Tracking.Api;

public partial class ShikiApiService : IShikiApiService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly ShikiTokenService _tokenService;
    private readonly ShikiHostResolver _hostResolver;
    private readonly HttpConditionalCache _httpCache;
    private readonly ShikiRateLimiter _rateLimiter;
    private readonly ShikiMirror? _fixedMirror;

    public string Name => _fixedMirror switch
    {
        ShikiMirror.Net => TrackerConstants.Names.ShikiFork,
        ShikiMirror.One => TrackerConstants.Names.ShikiOrig,
        _ => TrackerConstants.Names.ShikiGeneral
    };

    public string TrackerId => (_fixedMirror ?? _settingsService.Current.Api.ShikiMirror) == ShikiMirror.Net
        ? TrackerConstants.Ids.ShikiFork
        : TrackerConstants.Ids.ShikiOrig;

    // Token must belong to the currently active mirror or this fixed mirror
    public bool IsEnabled
    {
        get
        {
            var acc = _settingsService.Current.Api.GetAccount(TrackerId);
            if (acc != null)
                return acc.IsEnabled && acc.Tokens != null;

            var t = _settingsService.Current.Api.Shiki;
            return t != null && t.Mirror == (_fixedMirror ?? _settingsService.Current.Api.ShikiMirror);
        }
    }

    private ShikiMirror EffectiveMirror => _fixedMirror ?? _settingsService.Current.Api.ShikiMirror;
    private string ShikiBaseUrl => ShikiEndpoints.BaseUrl(EffectiveMirror);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> _malToShikiMap = new();

    private string ShikiWebsiteRoot
    {
        get
        {
            var raw = ShikiEndpoints.Host(EffectiveMirror).WebsiteUrl;
            if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            {
                var host = EffectiveMirror == ShikiMirror.Net && !string.IsNullOrEmpty(_hostResolver.ActiveForkHost)
                    ? _hostResolver.ActiveForkHost
                    : (EffectiveMirror == ShikiMirror.One && !string.IsNullOrEmpty(_hostResolver.ActiveOriginalHost)
                        ? _hostResolver.ActiveOriginalHost
                        : uri.Host);

                return new UriBuilder(uri) { Host = host, Path = string.Empty, Query = string.Empty }.Uri.GetLeftPart(UriPartial.Authority);
            }
            var fallback = ShikiEndpoints.Host(ShikiMirror.One).WebsiteUrl;
            return Uri.TryCreate(fallback, UriKind.Absolute, out var fallbackUri)
                ? fallbackUri.GetLeftPart(UriPartial.Authority)
                : string.Empty;
        }
    }

    private int ResolveShikiTargetId(int animeId)
    {
        return _malToShikiMap.TryGetValue(animeId, out var mapped) ? mapped : animeId;
    }


    public ShikiApiService(
        HttpClient httpClient,
        ISettingsService settingsService,
        ShikiTokenService tokenService,
        ShikiHostResolver hostResolver,
        IHttpCacheRepository httpCacheRepo,
        ShikiRateLimiter? rateLimiter = null,
        ShikiMirror? fixedMirror = null)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _tokenService = tokenService;
        _hostResolver = hostResolver;
        _rateLimiter = rateLimiter ?? new ShikiRateLimiter();
        _fixedMirror = fixedMirror;
        _httpCache = new HttpConditionalCache(
            _httpClient,
            httpCacheRepo,
            $"ShikiApi_{(_fixedMirror?.ToString() ?? "Dynamic")}",
            (client, request, innerCt) => SendRequestAsync(request, innerCt));
    }


    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ShikiBaseUrl + url.TrimStart('/'));
        return await SendRequestAsync(request, ct);
    }

    private async Task<SyncOutcome> PostAsync(string url, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, ShikiBaseUrl + url.TrimStart('/'))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        try
        {
            using var response = await SendRequestAsync(request, ct);
            var status = (int)response.StatusCode;
            if (status >= 200 && status < 300) return SyncOutcome.Success;
            if (status >= 500 || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                Log.Warning("ShikiApiService: transient {Status} for POST {Uri}", status, request.RequestUri);
                return SyncOutcome.TransientFailure;
            }
            Log.Warning("ShikiApiService: permanent {Status} for POST {Uri}", status, request.RequestUri);
            return SyncOutcome.PermanentFailure;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warning(ex, "ShikiApiService: PostAsync failed ({Uri})", request.RequestUri);
            return SyncOutcome.TransientFailure;
        }
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Add("User-Agent", AppInfo.UserAgent);
        var token = await _tokenService.EnsureValidTokenAsync(_fixedMirror, ct);
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Routed through ShikiHttp so the .net â‡„ .rip geo-redirect / 404
        // dance is handled transparently with method+body+auth preserved.
        return await ShikiHttp.SendShikiAsync(_httpClient, request, _hostResolver, ct);
    }
}
