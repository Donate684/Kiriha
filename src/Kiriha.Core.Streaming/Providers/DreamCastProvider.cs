using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Models;
using Serilog;

namespace Kiriha.Core.Streaming.Providers;

/// <summary>
/// Catalog and stream provider for Dream Cast studio (https://dreamerscast.com).
/// Provides high-quality multi-voice dubbing with direct HLS master.m3u8 stream playback.
/// </summary>
public sealed partial class DreamCastProvider : IStreamingProvider
{
    private readonly HttpClient _httpClient;
    private static readonly Regex AtobPlaylistRegex = GenerateAtobPlaylistRegex();

    public string ProviderName => StreamingConstants.Providers.DreamCast;

    public DreamCastProvider(HttpClient httpClient)
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
            var searchUrl = $"{StreamingConstants.DreamCast.BaseUrl}/";
            using var request = new HttpRequestMessage(HttpMethod.Post, searchUrl);
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Referer, StreamingConstants.DreamCast.RefererUrl);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);

            var pairs = new List<KeyValuePair<string, string>>
            {
                new("search", query.Trim()),
                new("status", ""),
                new("pageSize", "16"),
                new("pageNumber", "1")
            };
            request.Content = new FormUrlEncodedContent(pairs);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("[DreamCast] Search failed with status {Status} for '{Query}'", response.StatusCode, query);
                return [];
            }

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("releases", out var releasesProp) || releasesProp.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<StreamingSearchResult>();
            foreach (var item in releasesProp.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idProp))
                    continue;

                var id = idProp.ToString();
                var russian = item.TryGetProperty("russian", out var ruProp) ? ruProp.GetString() : null;
                var original = item.TryGetProperty("original", out var origProp) ? origProp.GetString() : null;
                var image = item.TryGetProperty("image", out var imgProp) ? imgProp.GetString() : null;
                var url = item.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;

                var title = !string.IsNullOrWhiteSpace(russian) ? russian.Trim() : (original?.Trim() ?? id);
                string? thumb = null;
                if (!string.IsNullOrWhiteSpace(image))
                {
                    thumb = image.StartsWith("//") ? $"{StreamingConstants.HttpsPrefix}{image[2..]}" : image;
                }

                var releasePath = !string.IsNullOrWhiteSpace(url) ? url : id;

                results.Add(new StreamingSearchResult
                {
                    Id = releasePath,
                    Title = title,
                    ThumbnailUrl = thumb,
                    Url = releasePath,
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
            Log.Warning(ex, "[DreamCast] Search error for '{Query}'", query);
            return [];
        }
    }

    public async Task<StreamingAnimeDetails?> GetAnimeDetailsAsync(string animeUrlOrId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(animeUrlOrId);

        try
        {
            string targetUrl;
            if (animeUrlOrId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                targetUrl = animeUrlOrId;
            }
            else if (animeUrlOrId.StartsWith('/'))
            {
                targetUrl = $"{StreamingConstants.DreamCast.BaseUrl}{animeUrlOrId}";
            }
            else if (animeUrlOrId.StartsWith("home/release/", StringComparison.OrdinalIgnoreCase))
            {
                targetUrl = $"{StreamingConstants.DreamCast.BaseUrl}/{animeUrlOrId}";
            }
            else
            {
                targetUrl = $"{StreamingConstants.DreamCast.BaseUrl}/home/release/{animeUrlOrId}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Referer, StreamingConstants.DreamCast.RefererUrl);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var match = AtobPlaylistRegex.Match(html);
            if (!match.Success)
            {
                Log.Warning("[DreamCast] Could not find atob playlist in page {Url}", targetUrl);
                return null;
            }

            var b64 = match.Groups["b64"].Value;
            var decodedBytes = Convert.FromBase64String(b64);
            var decodedString = Encoding.UTF8.GetString(decodedBytes);

            var streamUrls = decodedString.Split("||||", StringSplitOptions.RemoveEmptyEntries);
            var episodes = new List<StreamingEpisode>();

            for (int i = 0; i < streamUrls.Length; i++)
            {
                var epNum = i + 1;
                var streamUrl = streamUrls[i].Trim();
                if (string.IsNullOrWhiteSpace(streamUrl))
                    continue;

                episodes.Add(new StreamingEpisode
                {
                    EpisodeId = epNum.ToString(),
                    EpisodeNumber = epNum,
                    Title = $"Episode {epNum}",
                    Sources =
                    [
                        new StreamingSource
                        {
                            Title = "Dream Cast (1080p/720p HLS)",
                            PlayerUrl = streamUrl,
                            Dubber = "Dream Cast",
                            Language = "ru",
                            Provider = ProviderName
                        }
                    ]
                });
            }

            return new StreamingAnimeDetails
            {
                Id = animeUrlOrId,
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
            Log.Warning(ex, "[DreamCast] Failed to load details for '{Url}'", animeUrlOrId);
            return null;
        }
    }

    public Task<IReadOnlyList<StreamingSource>> GetEpisodeSourcesAsync(string animeId, int episodeNumber, CancellationToken ct = default)
    {
        // Episodes on DreamCast already contain their direct HLS sources populated from the release page playlist.
        return Task.FromResult<IReadOnlyList<StreamingSource>>([]);
    }

    [GeneratedRegex(@"atob\(\s*[""'](?<b64>[A-Za-z0-9+/=]+)[""']\s*\)", RegexOptions.Compiled)]
    private static partial Regex GenerateAtobPlaylistRegex();
}
