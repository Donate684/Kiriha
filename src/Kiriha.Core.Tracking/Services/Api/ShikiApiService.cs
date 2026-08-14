using System;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Shared;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Infrastructure;
using Kiriha.Infrastructure.Http;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Tracking.Auth;

using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;


namespace Kiriha.Core.Tracking.Api;

public partial class ShikiApiService : IShikiApiService
{
    private readonly HttpClient _httpClient;
    private readonly Kiriha.Core.Abstractions.Services.ISettingsService _settingsService;
    private readonly ShikiTokenService _tokenService;
    private readonly ShikiHostResolver _hostResolver;
    private readonly HttpConditionalCache _httpCache;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, (ShikiPersonResponse? Value, DateTime SystemDateTime)> _personCache = new();

    public string Name => "Shikimori";

    // Token must belong to the currently active mirror; otherwise it's effectively
    // useless because shikimori.one and shikimori.net are independent OAuth realms.
    public bool IsEnabled
    {
        get
        {
            var t = _settingsService.Current.Api.Shiki;
            return t != null && t.Mirror == _settingsService.Current.Api.ShikiMirror;
        }
    }

    private string ShikiBaseUrl => ShikiEndpoints.BaseUrl(_settingsService.Current.Api.ShikiMirror);

    public ShikiApiService(HttpClient httpClient, Kiriha.Core.Abstractions.Services.ISettingsService settingsService, ShikiTokenService tokenService, ShikiHostResolver hostResolver, Kiriha.Core.Abstractions.Repositories.IHttpCacheRepository httpCacheRepo)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _tokenService = tokenService;
        _hostResolver = hostResolver;
        _httpCache = new HttpConditionalCache(
            _httpClient,
            httpCacheRepo,
            "ShikiApi",
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
        var token = await _tokenService.EnsureValidTokenAsync(ct);
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Routed through ShikiHttp so the .net â‡„ .rip geo-redirect / 404
        // dance is handled transparently with method+body+auth preserved.
        return await ShikiHttp.SendShikiAsync(_httpClient, request, _hostResolver, ct);
    }
}
