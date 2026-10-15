using System.Text.Json;
using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Models;
using Serilog;

namespace Kiriha.Core.Streaming.Providers;

/// <summary>
/// Catalog and streaming provider for YummyAnime (https://api.yani.tv).
/// Offers vast anime library with multi-studio dubbing (Kodik players: Studio Band, AniDUB, Dream Cast, etc.).
/// </summary>
public sealed class YummyAnimeProvider : IStreamingProvider
{
    private readonly HttpClient _httpClient;

    public string ProviderName => StreamingConstants.Providers.YummyAnime;

    public YummyAnimeProvider(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<StreamingSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        try
        {
            var searchUrl = $"{StreamingConstants.YummyAnime.SearchEndpoint}?q={Uri.EscapeDataString(query.Trim())}";
            using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Accept, StreamingConstants.Headers.JsonAccept);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("[YummyAnime] Search failed with status {Status} for '{Query}'", response.StatusCode, query);
                return [];
            }

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("response", out var respProp) || respProp.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<StreamingSearchResult>();
            foreach (var item in respProp.EnumerateArray())
            {
                if (!item.TryGetProperty("anime_id", out var idProp))
                    continue;

                var id = idProp.ToString();
                var title = item.TryGetProperty("title", out var tProp) ? tProp.GetString() : id;
                var url = item.TryGetProperty("anime_url", out var uProp) ? uProp.GetString() : id;

                string? thumb = null;
                if (item.TryGetProperty("poster", out var posterProp))
                {
                    if (posterProp.TryGetProperty("medium", out var mProp))
                        thumb = mProp.GetString();
                    else if (posterProp.TryGetProperty("small", out var sProp))
                        thumb = sProp.GetString();
                    else if (posterProp.TryGetProperty("big", out var bProp))
                        thumb = bProp.GetString();
                }

                if (!string.IsNullOrWhiteSpace(thumb) && thumb.StartsWith("//"))
                {
                    thumb = $"{StreamingConstants.HttpsPrefix}{thumb[2..]}";
                }

                results.Add(new StreamingSearchResult
                {
                    Id = id,
                    Title = title ?? id,
                    ThumbnailUrl = thumb,
                    Url = url ?? id,
                    ProviderName = ProviderName
                });
            }

            return results;
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[YummyAnime] Search error for '{Query}'", query);
            return [];
        }
    }

    public async Task<StreamingAnimeDetails?> GetAnimeDetailsAsync(string animeUrlOrId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(animeUrlOrId);

        try
        {
            var animeId = ExtractAnimeId(animeUrlOrId);
            var videosUrl = $"{StreamingConstants.YummyAnime.SearchEndpoint}/{animeId}/videos";

            using var request = new HttpRequestMessage(HttpMethod.Get, videosUrl);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Accept, StreamingConstants.Headers.JsonAccept);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("response", out var respProp) || respProp.ValueKind != JsonValueKind.Array)
                return null;

            // Group videos by episode number
            var episodesMap = new Dictionary<int, List<StreamingSource>>();

            foreach (var v in respProp.EnumerateArray())
            {
                var numStr = v.TryGetProperty("number", out var numProp) ? numProp.GetString() : null;
                var iframeUrl = v.TryGetProperty("iframe_url", out var ifProp) ? ifProp.GetString() : null;

                if (string.IsNullOrWhiteSpace(iframeUrl))
                    continue;

                var epNum = int.TryParse(numStr, out var parsedNum) ? parsedNum : 1;

                string? dubber = null;
                string? player = null;
                if (v.TryGetProperty("data", out var dataProp))
                {
                    if (dataProp.TryGetProperty("dubbing", out var dProp))
                        dubber = dProp.GetString();
                    if (dataProp.TryGetProperty("player", out var pProp))
                        player = pProp.GetString();
                }

                var playerUrl = iframeUrl.StartsWith("//") ? $"{StreamingConstants.HttpsPrefix}{iframeUrl[2..]}" : iframeUrl;
                var sourceTitle = !string.IsNullOrWhiteSpace(dubber)
                    ? (!string.IsNullOrWhiteSpace(player) ? $"{dubber} ({player})" : dubber)
                    : (!string.IsNullOrWhiteSpace(player) ? player : "Player");

                var source = new StreamingSource
                {
                    Title = sourceTitle,
                    PlayerUrl = playerUrl,
                    Dubber = dubber ?? player,
                    Provider = ProviderName
                };

                if (!episodesMap.TryGetValue(epNum, out var list))
                {
                    list = [];
                    episodesMap[epNum] = list;
                }
                list.Add(source);
            }

            var episodes = episodesMap
                .OrderBy(kv => kv.Key)
                .Select(kv => new StreamingEpisode
                {
                    EpisodeId = kv.Key.ToString(),
                    EpisodeNumber = kv.Key,
                    Title = $"Episode {kv.Key}",
                    Sources = kv.Value
                })
                .ToList();

            return new StreamingAnimeDetails
            {
                Id = animeId,
                Title = animeUrlOrId,
                ProviderName = ProviderName,
                Episodes = episodes
            };
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[YummyAnime] Failed to load details for '{Id}'", animeUrlOrId);
            return null;
        }
    }

    public Task<IReadOnlyList<StreamingSource>> GetEpisodeSourcesAsync(string animeId, int episodeNumber, CancellationToken ct = default)
    {
        // Episodes in YummyAnime already have their full sources populated in GetAnimeDetailsAsync
        return Task.FromResult<IReadOnlyList<StreamingSource>>([]);
    }

    private static string ExtractAnimeId(string input)
    {
        var slashIndex = input.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex < input.Length - 1)
        {
            return input[(slashIndex + 1)..];
        }
        return input;
    }
}
