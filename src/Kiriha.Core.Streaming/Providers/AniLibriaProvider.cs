using System.Net;
using System.Text.Json;
using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Models;
using Serilog;

namespace Kiriha.Core.Streaming.Providers;

/// <summary>
/// Online streaming provider backed by AniLibria (Aniliberty REST API).
/// Provides official Russian dubs with direct 1080p/720p/480p HLS streams.
/// </summary>
public sealed class AniLibriaProvider : IStreamingProvider
{
    private readonly HttpClient _httpClient;

    public string ProviderName => StreamingConstants.Providers.AniLibria;

    public AniLibriaProvider(HttpClient httpClient)
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
            var searchUrl = $"{StreamingConstants.AniLibria.SearchEndpoint}?query={WebUtility.UrlEncode(query)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Accept, "application/json");
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Fallback to catalog search endpoint if dedicated search is unavailable
                return await SearchViaCatalogAsync(query, ct).ConfigureAwait(false);
            }

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<StreamingSearchResult>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var id = item.GetProperty("id").ToString();
                var mainTitle = item.TryGetProperty("name", out var nameProp) && nameProp.TryGetProperty("main", out var mainProp)
                    ? mainProp.GetString() ?? ""
                    : "";

                var thumb = ExtractPosterUrl(item);

                results.Add(new StreamingSearchResult
                {
                    Id = id,
                    Title = mainTitle,
                    ThumbnailUrl = thumb,
                    Url = $"{StreamingConstants.AniLibria.ReleaseDetailEndpoint}/{id}",
                    ProviderName = ProviderName
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AniLibria] Search failed for query '{Query}'", query);
            return [];
        }
    }

    private async Task<IReadOnlyList<StreamingSearchResult>> SearchViaCatalogAsync(string query, CancellationToken ct)
    {
        var catalogUrl = $"{StreamingConstants.AniLibria.CatalogReleasesEndpoint}?f[search]={WebUtility.UrlEncode(query)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, catalogUrl);
        request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Accept, "application/json");
        request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return [];

        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(content);

        if (!doc.RootElement.TryGetProperty("data", out var dataProp) || dataProp.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<StreamingSearchResult>();
        foreach (var item in dataProp.EnumerateArray())
        {
            var id = item.GetProperty("id").ToString();
            var mainTitle = item.TryGetProperty("name", out var nameProp) && nameProp.TryGetProperty("main", out var mainProp)
                ? mainProp.GetString() ?? ""
                : "";

                var thumb = ExtractPosterUrl(item);

            results.Add(new StreamingSearchResult
            {
                Id = id,
                Title = mainTitle,
                ThumbnailUrl = thumb,
                Url = $"{StreamingConstants.AniLibria.ReleaseDetailEndpoint}/{id}",
                ProviderName = ProviderName
            });
        }

        return results;
    }

    public async Task<StreamingAnimeDetails?> GetAnimeDetailsAsync(string animeUrlOrId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(animeUrlOrId);

        try
        {
            var id = ExtractReleaseId(animeUrlOrId);
            var detailUrl = $"{StreamingConstants.AniLibria.ReleaseDetailEndpoint}/{id}";

            using var request = new HttpRequestMessage(HttpMethod.Get, detailUrl);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Accept, "application/json");
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var title = root.TryGetProperty("name", out var nameProp) && nameProp.TryGetProperty("main", out var mainProp)
                ? mainProp.GetString() ?? ""
                : "";

            var description = root.TryGetProperty("description", out var descProp) ? descProp.GetString() : null;

            var thumb = ExtractPosterUrl(root);

            var episodes = new List<StreamingEpisode>();
            if (root.TryGetProperty("episodes", out var episodesProp) && episodesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var ep in episodesProp.EnumerateArray())
                {
                    var ordinal = ep.TryGetProperty("ordinal", out var ordProp) && ordProp.TryGetInt32(out var ord) ? ord : episodes.Count + 1;
                    var epTitle = ep.TryGetProperty("name", out var epNameProp) && epNameProp.GetString() is { } name && !string.IsNullOrWhiteSpace(name)
                        ? name
                        : $"Episode {ordinal}";

                    var sources = new List<StreamingSource>();

                    if (ep.TryGetProperty("hls_1080", out var hls1080) && hls1080.GetString() is { } s1080 && !string.IsNullOrWhiteSpace(s1080))
                    {
                        sources.Add(new StreamingSource
                        {
                            Title = "AniLibria (1080p)",
                            PlayerUrl = s1080,
                            Dubber = "AniLibria",
                            Language = "ru",
                            Provider = ProviderName
                        });
                    }

                    if (ep.TryGetProperty("hls_720", out var hls720) && hls720.GetString() is { } s720 && !string.IsNullOrWhiteSpace(s720))
                    {
                        sources.Add(new StreamingSource
                        {
                            Title = "AniLibria (720p)",
                            PlayerUrl = s720,
                            Dubber = "AniLibria",
                            Language = "ru",
                            Provider = ProviderName
                        });
                    }

                    if (ep.TryGetProperty("hls_480", out var hls480) && hls480.GetString() is { } s480 && !string.IsNullOrWhiteSpace(s480))
                    {
                        sources.Add(new StreamingSource
                        {
                            Title = "AniLibria (480p)",
                            PlayerUrl = s480,
                            Dubber = "AniLibria",
                            Language = "ru",
                            Provider = ProviderName
                        });
                    }

                    episodes.Add(new StreamingEpisode
                    {
                        EpisodeNumber = ordinal,
                        Title = epTitle,
                        EpisodeId = ep.TryGetProperty("id", out var idProp) ? idProp.ToString() : null,
                        Sources = sources
                    });
                }
            }

            return new StreamingAnimeDetails
            {
                Id = id,
                Title = title,
                Description = description,
                ThumbnailUrl = thumb,
                ProviderName = ProviderName,
                Episodes = episodes
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AniLibria] Failed to get details for {UrlOrId}", animeUrlOrId);
            return null;
        }
    }

    public async Task<IReadOnlyList<StreamingSource>> GetEpisodeSourcesAsync(string animeId, int episodeNumber, CancellationToken ct = default)
    {
        var details = await GetAnimeDetailsAsync(animeId, ct).ConfigureAwait(false);
        var episode = details?.Episodes.FirstOrDefault(e => e.EpisodeNumber == episodeNumber);
        return episode?.Sources ?? [];
    }

    private static string ExtractReleaseId(string animeUrlOrId)
    {
        var parts = animeUrlOrId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts[^1];
    }

    private static string? ExtractPosterUrl(JsonElement item)
    {
        if (!item.TryGetProperty("poster", out var posterProp))
            return null;

        string? raw = null;

        if (posterProp.TryGetProperty("optimized", out var optProp))
        {
            if (optProp.TryGetProperty("preview", out var optPrevProp))
                raw = optPrevProp.GetString();
            else if (optProp.TryGetProperty("src", out var optSrcProp))
                raw = optSrcProp.GetString();
            else if (optProp.TryGetProperty("thumbnail", out var optThumbProp))
                raw = optThumbProp.GetString();
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            if (posterProp.TryGetProperty("preview", out var prevProp))
                raw = prevProp.GetString();
            else if (posterProp.TryGetProperty("src", out var srcProp))
                raw = srcProp.GetString();
            else if (posterProp.TryGetProperty("thumbnail", out var thumbProp))
                raw = thumbProp.GetString();
        }

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (raw.StartsWith(StreamingConstants.HttpPrefix, StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith(StreamingConstants.HttpsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        return raw.StartsWith('/')
            ? $"{StreamingConstants.AniLibria.StaticHost}{raw}"
            : $"{StreamingConstants.AniLibria.StaticHost}/{raw}";
    }
}
