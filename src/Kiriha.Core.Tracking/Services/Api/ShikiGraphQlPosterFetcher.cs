using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Infrastructure.Http;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

internal static class ShikiGraphQlPosterFetcher
{
    public static async Task<Dictionary<int, string>> FetchPostersAsync(
        IEnumerable<int> ids,
        bool isManga,
        string shikiBaseUrl,
        HttpConditionalCache httpCache,
        ConcurrentDictionary<string, string>? posterMemoryCache,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
        CancellationToken ct = default)
    {
        var posterMap = new Dictionary<int, string>();
        var idList = ids.Where(id => id > 0).Distinct().ToList();
        if (idList.Count == 0) return posterMap;

        string entityType = isManga ? "mangas" : "animes";
        var neededIds = new List<int>();

        foreach (var id in idList)
        {
            var memKey = $"{entityType}_{id}";
            if (posterMemoryCache != null &&
                posterMemoryCache.TryGetValue(memKey, out var memUrl) &&
                !string.IsNullOrEmpty(memUrl) &&
                !AnimeEntity.IsMissingPosterUrl(memUrl))
            {
                posterMap[id] = memUrl;
            }
            else
            {
                neededIds.Add(id);
            }
        }

        if (neededIds.Count == 0) return posterMap;

        var toQueryFromNetwork = new List<int>();
        foreach (var id in neededIds)
        {
            if (ct.IsCancellationRequested) break;
            var dbKey = $"shiki_poster_{entityType}_{id}";
            var cachedBytes = await httpCache.GetCachedBodyAsync(dbKey, ct);
            if (cachedBytes != null && cachedBytes.Length > 0)
            {
                var cachedUrl = Encoding.UTF8.GetString(cachedBytes);
                if (!string.IsNullOrEmpty(cachedUrl) && !AnimeEntity.IsMissingPosterUrl(cachedUrl))
                {
                    if (posterMemoryCache != null) posterMemoryCache[$"{entityType}_{id}"] = cachedUrl;
                    posterMap[id] = cachedUrl;
                    continue;
                }
            }
            toQueryFromNetwork.Add(id);
        }

        if (toQueryFromNetwork.Count == 0) return posterMap;

        foreach (var chunk in toQueryFromNetwork.Chunk(50))
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var idsStr = string.Join(",", chunk);
                var query = $"{{\"query\":\"{{ {entityType}(ids: \\\"{idsStr}\\\", limit: {chunk.Length}) {{ id poster {{ originalUrl }} }} }}\"}}";
                using var request = new HttpRequestMessage(HttpMethod.Post, shikiBaseUrl + "graphql")
                {
                    Content = new StringContent(query, Encoding.UTF8, "application/json")
                };

                using var response = await sendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Debug("ShikiGraphQlPosterFetcher: GraphQL poster fetch returned HTTP {Status}", response.StatusCode);
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
                        int id = 0;
                        if (entry.TryGetProperty("id", out var idProp))
                        {
                            if (idProp.ValueKind == JsonValueKind.String)
                                int.TryParse(idProp.GetString(), out id);
                            else if (idProp.ValueKind == JsonValueKind.Number)
                                idProp.TryGetInt32(out id);
                        }
                        if (id <= 0 && chunk.Length == 1)
                        {
                            id = chunk[0];
                        }
                        if (id <= 0) continue;

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
                                if (posterMemoryCache != null) posterMemoryCache[$"{entityType}_{id}"] = url;
                                _ = httpCache.SetCachedBodyAsync($"shiki_poster_{entityType}_{id}", Encoding.UTF8.GetBytes(url), CancellationToken.None);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Debug(ex, "ShikiGraphQlPosterFetcher: Exception during GraphQL poster fetch chunk");
            }
        }

        return posterMap;
    }
}
