using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kiriha.Infrastructure.Http;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Feed;

public class NyaaFeedClient
{
    private readonly HttpClient _httpClient;
    private readonly HttpConditionalCache _httpCache;

    public NyaaFeedClient(IHttpClientFactory httpClientFactory, IHttpCacheRepository httpCacheRepo)
    {
        _httpClient = httpClientFactory.CreateClient("RssClient");
        _httpCache = new HttpConditionalCache(_httpClient, httpCacheRepo, "Nyaa");
    }

    public Task<XDocument?> FetchGlobalFeedAsync(CancellationToken ct)
    {
        return FetchRssDocumentAsync("https://nyaa.si/?page=rss&c=1_2", ct);
    }

    public Task<XDocument?> FetchSearchAsync(string query, CancellationToken ct)
    {
        return FetchRssDocumentAsync($"https://nyaa.si/?page=rss&q={Uri.EscapeDataString(query)}&c=1_2", ct);
    }

    private async Task<XDocument?> FetchRssDocumentAsync(string url, CancellationToken ct)
    {
        var bytes = await _httpCache.SendAsync(
            requestFactory: innerCt =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                return Task.FromResult(request);
            },
            ct: ct);

        if (bytes == null || bytes.Length == 0) return null;

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            return XDocument.Load(ms);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NyaaFeedClient: failed to parse Nyaa RSS XML for {Url}", url);
            return null;
        }
    }
}
