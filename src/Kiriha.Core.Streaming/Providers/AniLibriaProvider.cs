using System.Net;
using System.Text.Json;
using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Models;
using Kiriha.Core.Streaming.Services;
using Serilog;

namespace Kiriha.Core.Streaming.Providers;

/// <summary>Provides AniLibria catalog entries, episode lists and direct HLS streams.</summary>
public sealed class AniLibriaProvider(HttpClient httpClient) : IStreamingProvider
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public string ProviderName => StreamingConstants.Providers.AniLibria;

    public async Task<IReadOnlyList<StreamingSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var encodedQuery = WebUtility.UrlEncode(query);
        try
        {
            using var results = await GetJsonAsync(
                $"{StreamingConstants.AniLibria.SearchEndpoint}?query={encodedQuery}", ct).ConfigureAwait(false);
            if (results?.RootElement.ValueKind == JsonValueKind.Array)
            {
                var parsed = ParseSearchResults(results.RootElement, ct);
                if (parsed.Count > 0 || results.RootElement.GetArrayLength() == 0)
                    return parsed;
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException)
        {
            Log.Debug(ex, "[AniLibria] Dedicated search unavailable. Trying the catalog.");
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            using var catalog = await GetJsonAsync(
                $"{StreamingConstants.AniLibria.CatalogReleasesEndpoint}?f[search]={encodedQuery}", ct).ConfigureAwait(false);
            return catalog != null && Property(catalog.RootElement, "data").ValueKind == JsonValueKind.Array
                ? ParseSearchResults(Property(catalog.RootElement, "data"), ct)
                : [];
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException)
        {
            Log.Warning(ex, "[AniLibria] Catalog search failed");
            return [];
        }
    }

    private IReadOnlyList<StreamingSearchResult> ParseSearchResults(JsonElement rows, CancellationToken ct)
    {
        var results = new List<StreamingSearchResult>();
        foreach (var item in rows.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var id = ReadId(item);
            var title = ReadString(Property(item, "name"), "main");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                continue;

            results.Add(new StreamingSearchResult
            {
                Id = id,
                Title = title,
                ThumbnailUrl = ExtractPosterUrl(item),
                Url = $"{StreamingConstants.AniLibria.ReleaseDetailEndpoint}/{Uri.EscapeDataString(id)}",
                ProviderName = ProviderName
            });
        }

        return results;
    }

    public async Task<StreamingAnimeDetails?> GetAnimeDetailsAsync(string animeUrlOrId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(animeUrlOrId);
        var path = Uri.TryCreate(animeUrlOrId, UriKind.Absolute, out var uri) ? uri.AbsolutePath : animeUrlOrId;
        var id = path.TrimEnd('/').Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(id))
            return null;

        try
        {
            using var doc = await GetJsonAsync(
                $"{StreamingConstants.AniLibria.ReleaseDetailEndpoint}/{Uri.EscapeDataString(id)}", ct).ConfigureAwait(false);
            if (doc?.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var root = doc.RootElement;
            var episodes = new List<StreamingEpisode>();
            var rows = Property(root, "episodes");
            if (rows.ValueKind == JsonValueKind.Array)
            {
                int position = 0;
                foreach (var row in rows.EnumerateArray())
                {
                    ct.ThrowIfCancellationRequested();
                    position++;
                    if (row.ValueKind == JsonValueKind.Object)
                        episodes.Add(ParseEpisode(row, position));
                }
            }

            return new StreamingAnimeDetails
            {
                Id = ReadId(root) ?? id,
                Title = ReadString(Property(root, "name"), "main") ?? string.Empty,
                Description = ReadString(root, "description"),
                ThumbnailUrl = ExtractPosterUrl(root),
                ProviderName = ProviderName,
                Episodes = episodes
            };
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException)
        {
            Log.Warning(ex, "[AniLibria] Failed to load release details");
            return null;
        }
    }

    private StreamingEpisode ParseEpisode(JsonElement row, int position)
    {
        var ordinal = Property(row, "ordinal");
        var number = ordinal.ValueKind == JsonValueKind.Number && ordinal.TryGetInt32(out var parsed) ? parsed : position;
        var title = ReadString(row, "name");
        var sources = new List<StreamingSource>();
        foreach (int quality in new[] { 1080, 720, 480 })
        {
            var url = ReadString(row, $"hls_{quality}");
            if (StreamingMediaUrl.TryParse(url, out _))
                sources.Add(new StreamingSource
                {
                    Title = $"AniLibria ({quality}p)",
                    PlayerUrl = StreamingMediaUrl.Normalize(url),
                    Dubber = ProviderName,
                    Language = "ru",
                    Provider = ProviderName
                });
        }

        return new StreamingEpisode
        {
            EpisodeNumber = number,
            Title = string.IsNullOrWhiteSpace(title) ? $"Episode {number}" : title,
            EpisodeId = ReadId(row),
            Sources = sources
        };
    }

    public async Task<IReadOnlyList<StreamingSource>> GetEpisodeSourcesAsync(string animeId, int episodeNumber, CancellationToken ct = default)
    {
        var details = await GetAnimeDetailsAsync(animeId, ct).ConfigureAwait(false);
        var episode = details?.Episodes.FirstOrDefault(e => e.EpisodeNumber == episodeNumber);
        return episode?.Sources ?? [];
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Accept, StreamingConstants.Headers.JsonMediaType);
        request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private static JsonElement Property(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) ? value : default;

    private static string? ReadString(JsonElement item, string name)
    {
        var value = Property(item, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string? ReadId(JsonElement item)
    {
        var id = Property(item, "id");
        return id.ValueKind is JsonValueKind.Number or JsonValueKind.String ? id.ToString() : null;
    }

    private static string? ExtractPosterUrl(JsonElement item)
    {
        var poster = Property(item, "poster");
        var optimized = Property(poster, "optimized");
        var raw = ReadString(optimized, "preview") ?? ReadString(optimized, "src") ?? ReadString(optimized, "thumbnail");
        if (string.IsNullOrWhiteSpace(raw))
            raw = ReadString(poster, "preview") ?? ReadString(poster, "src") ?? ReadString(poster, "thumbnail");

        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (StreamingMediaUrl.TryParse(raw, out _))
            return StreamingMediaUrl.Normalize(raw);
        return Uri.TryCreate(new Uri(StreamingConstants.AniLibria.StaticHost), raw, out var resolved) &&
            StreamingMediaUrl.IsHttp(resolved.AbsoluteUri) ? resolved.AbsoluteUri : null;
    }
}
