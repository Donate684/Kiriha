using System.Text.Json;
using System.Text.RegularExpressions;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Models;
using Kiriha.Core.Streaming.Services;
using Serilog;

namespace Kiriha.Core.Streaming.Providers;

/// <summary>
/// Catalog and stream provider for Anime 365 / Smotret Anime (https://smotret-anime.org).
/// Premium aggregator requiring an account and application access token for full stream playback.
/// Uses <see cref="Anime365HostResolver"/> for dynamic mirror failover across official mirrors.
/// </summary>
public sealed partial class Anime365Provider : IStreamingProvider
{
    private readonly HttpClient _httpClient;
    private readonly Anime365HostResolver _hostResolver;
    private readonly ISettingsService? _settingsService;

    public string ProviderName => StreamingConstants.Providers.Anime365;

    public Anime365Provider(
        HttpClient httpClient,
        Anime365HostResolver? hostResolver = null,
        ISettingsService? settingsService = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _settingsService = settingsService;
        _hostResolver = hostResolver ?? new Anime365HostResolver(settingsService);
    }

    public async Task<IReadOnlyList<StreamingSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        try
        {
            var searchUrl = $"{StreamingConstants.Anime365.SeriesEndpoint}?query={Uri.EscapeDataString(query.Trim())}&limit=20";
            var token = _settingsService?.Current.Torrents.OnlineStreaming.Anime365Token;
            if (!string.IsNullOrWhiteSpace(token))
            {
                searchUrl += $"&access_token={Uri.EscapeDataString(token)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            AddDefaultHeaders(request);

            using var response = await _hostResolver.SendWithFailoverAsync(_httpClient, request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("[Anime365] Search failed with status {Status} for '{Query}'", response.StatusCode, query);
                return [];
            }

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("data", out var dataProp) || dataProp.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<StreamingSearchResult>();
            foreach (var item in dataProp.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var id = SafeGetId(item);
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var title = ExtractTitle(item);
                var rawPoster = ExtractPoster(item);
                var poster = !string.IsNullOrWhiteSpace(rawPoster) ? _hostResolver.Rewrite(rawPoster) : null;

                results.Add(new StreamingSearchResult
                {
                    Id = id,
                    Title = title,
                    ThumbnailUrl = poster,
                    Url = _hostResolver.Rewrite($"{StreamingConstants.Anime365.BaseUrl}/series/{id}"),
                    ProviderName = ProviderName
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Anime365] Search error for '{Query}'", query);
            return [];
        }
    }

    public async Task<StreamingAnimeDetails?> GetAnimeDetailsAsync(string animeUrlOrId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(animeUrlOrId);

        try
        {
            var id = ExtractSeriesId(animeUrlOrId);
            if (string.IsNullOrWhiteSpace(id))
                return null;

            var detailUrl = $"{StreamingConstants.Anime365.SeriesEndpoint}/{id}";
            var token = _settingsService?.Current.Torrents.OnlineStreaming.Anime365Token;
            if (!string.IsNullOrWhiteSpace(token))
            {
                detailUrl += $"?access_token={Uri.EscapeDataString(token)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, detailUrl);
            AddDefaultHeaders(request);

            using var response = await _hostResolver.SendWithFailoverAsync(_httpClient, request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("[Anime365] Details failed with status {Status} for '{Id}'", response.StatusCode, id);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("data", out var dataProp) || dataProp.ValueKind != JsonValueKind.Object)
                return null;

            var title = ExtractTitle(dataProp);
            var rawPoster = ExtractPoster(dataProp);
            var poster = !string.IsNullOrWhiteSpace(rawPoster) ? _hostResolver.Rewrite(rawPoster) : null;
            var description = ExtractDescription(dataProp);

            var episodes = new List<StreamingEpisode>();
            if (dataProp.TryGetProperty("episodes", out var epListProp) && epListProp.ValueKind == JsonValueKind.Array)
            {
                var ordinal = 1;
                foreach (var ep in epListProp.EnumerateArray())
                {
                    if (ep.ValueKind != JsonValueKind.Object)
                        continue;

                    if (!SafeIsActive(ep))
                        continue;

                    var epId = SafeGetId(ep);
                    var epInt = SafeGetInt(ep, "episodeInt", ordinal);
                    if (epInt < 0) epInt = ordinal;

                    var epTitle = SafeGetString(ep, "episodeTitle");
                    if (string.IsNullOrWhiteSpace(epTitle))
                    {
                        var epFull = SafeGetString(ep, "episodeFull");
                        if (!string.IsNullOrWhiteSpace(epFull))
                            epTitle = epFull.Trim();
                        else
                            epTitle = $"Episode {epInt}";
                    }

                    episodes.Add(new StreamingEpisode
                    {
                        EpisodeNumber = epInt,
                        Title = epTitle,
                        EpisodeId = epId,
                        Sources = []
                    });
                    ordinal++;
                }

                episodes = episodes.OrderBy(e => e.EpisodeNumber).ToList();
            }

            return new StreamingAnimeDetails
            {
                Id = id,
                Title = title,
                Description = description,
                ThumbnailUrl = poster,
                ProviderName = ProviderName,
                Episodes = episodes
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Anime365] Failed to fetch details for '{Id}'", animeUrlOrId);
            return null;
        }
    }

    public async Task<IReadOnlyList<StreamingSource>> GetEpisodeSourcesAsync(string animeId, int episodeNumber, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(animeId);

        try
        {
            var cleanId = ExtractSeriesId(animeId);
            if (string.IsNullOrWhiteSpace(cleanId) || !int.TryParse(cleanId, out var parsedId))
                return [];

            bool looksLikeSeriesUrl = animeId.Contains("/catalog/", StringComparison.OrdinalIgnoreCase)
                                   || animeId.Contains("/series/", StringComparison.OrdinalIgnoreCase);

            // Episode and series ids share the same numeric range. Keep episode requests
            // scoped to that episode even when its translation list is empty.
            if (!looksLikeSeriesUrl)
                return await FetchTranslationsForEpisodeAsync(parsedId, ct).ConfigureAwait(false);

            // 2. Series lookup path: animeId is a series URL or seriesId; locate target episodeId for episodeNumber.
            var detailUrl = $"{StreamingConstants.Anime365.SeriesEndpoint}/{cleanId}";
            var token = _settingsService?.Current.Torrents.OnlineStreaming.Anime365Token;
            if (!string.IsNullOrWhiteSpace(token))
            {
                detailUrl += $"?access_token={Uri.EscapeDataString(token)}";
            }

            using var detailRequest = new HttpRequestMessage(HttpMethod.Get, detailUrl);
            AddDefaultHeaders(detailRequest);

            using var detailResponse = await _hostResolver.SendWithFailoverAsync(_httpClient, detailRequest, ct).ConfigureAwait(false);
            if (detailResponse.IsSuccessStatusCode)
            {
                var detailContent = await detailResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var detailDoc = JsonDocument.Parse(detailContent);
                if (detailDoc.RootElement.TryGetProperty("data", out var seriesData) && seriesData.ValueKind == JsonValueKind.Object)
                {
                    int? targetEpisodeId = null;
                    if (seriesData.TryGetProperty("episodes", out var epArray) && epArray.ValueKind == JsonValueKind.Array)
                    {
                        var index = 1;
                        foreach (var ep in epArray.EnumerateArray())
                        {
                            if (ep.ValueKind != JsonValueKind.Object || !SafeIsActive(ep))
                                continue;

                            var epInt = SafeGetInt(ep, "episodeInt", index);
                            if (epInt < 0) epInt = index;

                            if (epInt == episodeNumber)
                            {
                                var epIdStr = SafeGetId(ep);
                                if (!string.IsNullOrWhiteSpace(epIdStr) && int.TryParse(epIdStr, out var epIdVal))
                                {
                                    targetEpisodeId = epIdVal;
                                    break;
                                }
                            }
                            index++;
                        }
                    }

                    if (targetEpisodeId.HasValue)
                    {
                        return await FetchTranslationsForEpisodeAsync(targetEpisodeId.Value, ct).ConfigureAwait(false);
                    }
                }
            }

            return [];
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Anime365] Failed to get episode sources for AnimeId '{AnimeId}', Ep {Ep}", animeId, episodeNumber);
            return [];
        }
    }

    private async Task<IReadOnlyList<StreamingSource>> FetchTranslationsForEpisodeAsync(int episodeId, CancellationToken ct)
    {
        var translationsUrl = $"{StreamingConstants.Anime365.TranslationsEndpoint}?episodeId={episodeId}&limit=50";
        var token = _settingsService?.Current.Torrents.OnlineStreaming.Anime365Token;
        if (!string.IsNullOrWhiteSpace(token))
        {
            translationsUrl += $"&access_token={Uri.EscapeDataString(token)}";
        }

        using var transRequest = new HttpRequestMessage(HttpMethod.Get, translationsUrl);
        AddDefaultHeaders(transRequest);

        using var transResponse = await _hostResolver.SendWithFailoverAsync(_httpClient, transRequest, ct).ConfigureAwait(false);
        if (!transResponse.IsSuccessStatusCode)
            return [];

        var transContent = await transResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var transDoc = JsonDocument.Parse(transContent);
        if (!transDoc.RootElement.TryGetProperty("data", out var transArray) || transArray.ValueKind != JsonValueKind.Array)
            return [];

        var sources = new List<StreamingSource>();

        foreach (var t in transArray.EnumerateArray())
        {
            if (t.ValueKind != JsonValueKind.Object)
                continue;

            if (!SafeIsActive(t))
                continue;

            var transId = SafeGetId(t);
            if (string.IsNullOrWhiteSpace(transId))
                continue;

            var authorsSummary = SafeGetString(t, "authorsSummary");
            var typeKind = SafeGetString(t, "typeKind");
            var typeLang = SafeGetString(t, "typeLang");
            var type = SafeGetString(t, "type");
            var height = SafeGetInt(t, "height", 720);

            var lang = !string.IsNullOrWhiteSpace(typeLang)
                ? typeLang.Trim().ToLowerInvariant()
                : ExtractLangFromType(type);

            var dubberName = !string.IsNullOrWhiteSpace(authorsSummary) ? authorsSummary.Trim() : "Anime365";
            var quality = height >= 1080 ? "1080p" : (height >= 720 ? "720p" : (height > 0 ? $"{height}p" : "720p"));

            var isSub = string.Equals(typeKind, "sub", StringComparison.OrdinalIgnoreCase);
            var isRaw = string.Equals(typeKind, "raw", StringComparison.OrdinalIgnoreCase);
            var kindTag = isSub ? " (Sub)" : (isRaw ? " (Raw)" : string.Empty);
            var langTag = !string.IsNullOrWhiteSpace(lang) ? $" [{lang.ToUpperInvariant()}]" : string.Empty;

            var sourceTitle = $"{dubberName}{kindTag}{langTag} [{quality}]";

            var playerUrl = $"{StreamingConstants.Anime365.EmbedEndpoint}/{transId}";
            if (!string.IsNullOrWhiteSpace(token))
            {
                playerUrl += $"?access_token={Uri.EscapeDataString(token)}";
            }

            sources.Add(new StreamingSource
            {
                Title = sourceTitle,
                PlayerUrl = _hostResolver.Rewrite(playerUrl),
                Dubber = dubberName,
                Quality = quality,
                Language = lang,
                Provider = ProviderName
            });
        }

        return sources;
    }

    private static string? ExtractLangFromType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;
        if (type.EndsWith("Ru", StringComparison.OrdinalIgnoreCase)) return "ru";
        if (type.EndsWith("En", StringComparison.OrdinalIgnoreCase)) return "en";
        if (type.EndsWith("Ja", StringComparison.OrdinalIgnoreCase)) return "ja";
        if (string.Equals(type, "raw", StringComparison.OrdinalIgnoreCase)) return "ja";
        return null;
    }
}
