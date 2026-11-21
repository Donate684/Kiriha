using System.Xml.Linq;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Tracking.Api;
using Serilog;

namespace Kiriha.Core.Tracking.Feed;

public class NyaaFeedClient
{
    private static readonly TimeSpan FeedSearchTtl = TimeSpan.FromMinutes(1);
    private readonly HttpConditionalCache _httpCache;

    public NyaaFeedClient(IHttpClientFactory httpClientFactory, IHttpCacheRepository httpCacheRepo, TimeProvider? clock = null)
    {
        _httpCache = new HttpConditionalCache(httpClientFactory.CreateClient("RssClient"), httpCacheRepo, "Nyaa", clock);
    }

    public Task<XDocument?> FetchSearchAsync(string query, CancellationToken ct)
    {
        return FetchRssDocumentAsync($"{Kiriha.Core.Domain.Constants.AppConstants.Api.Nyaa.BaseUrl}?page=rss&q={Uri.EscapeDataString(query)}&c=1_2", ct);
    }

    private async Task<XDocument?> FetchRssDocumentAsync(string url, CancellationToken ct)
    {
        var bytes = await _httpCache.SendAsync(
            url,
            requestFactory: _ => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, url)),
            ct: ct,
            localTtl: FeedSearchTtl);

        if (bytes is null || bytes.Length == 0) return null;

        try
        {
            using var ms = new ReadOnlyMemoryStream(bytes);
            return XDocument.Load(ms);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NyaaFeedClient: failed to parse Nyaa RSS XML for {Url}", url);
            return null;
        }
    }
}
