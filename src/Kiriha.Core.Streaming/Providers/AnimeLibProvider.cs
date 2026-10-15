using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Helpers;
using Kiriha.Core.Streaming.Models;
using Serilog;

namespace Kiriha.Core.Streaming.Providers;

/// <summary>
/// Catalog provider for AnimeLib / MangaLib network (https://api.cdnlibs.org/api).
/// Delivers rich anime catalog with multi-studio dubbing (Studio Band, AniDUB, Dream Cast, DEEP, Flarrow, subs).
/// Supports authenticated sessions to stream direct 1080p MP4 from AnimeLib CDN servers.
/// </summary>
public sealed class AnimeLibProvider : IStreamingProvider
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService? _settingsService;

    public string ProviderName => StreamingConstants.Providers.AnimeLib;

    public AnimeLibProvider(HttpClient httpClient, ISettingsService? settingsService = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _settingsService = settingsService;
    }

    public async Task<IReadOnlyList<StreamingSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        try
        {
            var searchUrl = $"{StreamingConstants.AnimeLib.SearchEndpoint}?q={Uri.EscapeDataString(query.Trim())}";
            using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            AddDefaultAnimeLibHeaders(request);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("[AnimeLib] Search failed with status {Status} for '{Query}'", response.StatusCode, query);
                return [];
            }

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("data", out var dataProp) || dataProp.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<StreamingSearchResult>();
            foreach (var item in dataProp.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idProp))
                    continue;

                var id = idProp.ToString();
                var rusName = item.TryGetProperty("rus_name", out var rProp) ? rProp.GetString() : null;
                var origName = item.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
                var engName = item.TryGetProperty("eng_name", out var eProp) ? eProp.GetString() : null;
                var slugUrl = item.TryGetProperty("slug_url", out var sProp) ? sProp.GetString() : id;

                var title = !string.IsNullOrWhiteSpace(rusName)
                    ? rusName
                    : (!string.IsNullOrWhiteSpace(origName) ? origName : engName ?? id);

                string? thumb = null;
                if (item.TryGetProperty("cover", out var coverProp))
                {
                    if (coverProp.TryGetProperty("default", out var dProp))
                        thumb = dProp.GetString();
                    else if (coverProp.TryGetProperty("thumbnail", out var tProp))
                        thumb = tProp.GetString();
                }

                results.Add(new StreamingSearchResult
                {
                    Id = id,
                    Title = title,
                    ThumbnailUrl = thumb,
                    Url = slugUrl ?? id,
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
            Log.Warning(ex, "[AnimeLib] Search error for '{Query}'", query);
            return [];
        }
    }

    public async Task<StreamingAnimeDetails?> GetAnimeDetailsAsync(string animeUrlOrId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(animeUrlOrId);

        try
        {
            var animeId = ExtractAnimeId(animeUrlOrId);
            var epUrl = $"{StreamingConstants.AnimeLib.EpisodesEndpoint}?anime_id={animeId}";

            using var request = new HttpRequestMessage(HttpMethod.Get, epUrl);
            var token = _settingsService?.Current.Torrents.OnlineStreaming.AnimeLibToken;
            AddDefaultAnimeLibHeaders(request, token);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("data", out var dataProp) || dataProp.ValueKind != JsonValueKind.Array)
                return null;

            var episodes = new List<StreamingEpisode>();
            foreach (var item in dataProp.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var epIdProp))
                    continue;

                var epId = epIdProp.ToString();
                var numberStr = item.TryGetProperty("number", out var numProp) ? numProp.GetString() : null;
                var epName = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;

                var epNum = int.TryParse(numberStr, out var parsedNum) ? parsedNum : episodes.Count + 1;
                var epTitle = !string.IsNullOrWhiteSpace(epName) ? epName : $"Episode {epNum}";

                episodes.Add(new StreamingEpisode
                {
                    EpisodeId = epId,
                    EpisodeNumber = epNum,
                    Title = epTitle,
                    Sources = []
                });
            }

            episodes.Sort((a, b) => a.EpisodeNumber.CompareTo(b.EpisodeNumber));

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
            Log.Warning(ex, "[AnimeLib] Failed to load details for '{Id}'", animeUrlOrId);
            return null;
        }
    }

    public async Task<IReadOnlyList<StreamingSource>> GetEpisodeSourcesAsync(string animeId, int episodeNumber, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(animeId);

        try
        {
            // If animeId passed is directly episodeId:
            var epDetailsUrl = $"{StreamingConstants.AnimeLib.EpisodesEndpoint}/{animeId}";
            using var request = new HttpRequestMessage(HttpMethod.Get, epDetailsUrl);

            var token = _settingsService?.Current.Torrents.OnlineStreaming.AnimeLibToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                token = Environment.GetEnvironmentVariable("ANIMELIB_TOKEN");
            }
            AddDefaultAnimeLibHeaders(request, token);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return [];

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("data", out var dataProp) ||
                !dataProp.TryGetProperty("players", out var playersProp) ||
                playersProp.ValueKind != JsonValueKind.Array)
                return [];

            var sources = new List<StreamingSource>();
            foreach (var p in playersProp.EnumerateArray())
            {
                var playerType = p.TryGetProperty("player", out var ptProp) ? ptProp.GetString() : "Player";
                string? teamName = null;
                if (p.TryGetProperty("team", out var teamProp) && teamProp.TryGetProperty("name", out var tnProp))
                {
                    teamName = tnProp.GetString();
                }

                // Check for native AnimeLib CDN player (unlocked via auth token)
                var isAnimelib = string.Equals(playerType, "animelib", StringComparison.OrdinalIgnoreCase);
                if (isAnimelib && p.TryGetProperty("video", out var videoProp) &&
                    videoProp.TryGetProperty("quality", out var qualityProp) &&
                    qualityProp.ValueKind == JsonValueKind.Array)
                {
                    var videoDomain = p.TryGetProperty("video_domain", out var vdProp) ? vdProp.GetString() : null;
                    var baseServerUrl = ResolveVideoServerUrl(videoDomain);

                    // Extract external subtitle tracks if present (prefer ASS format for styling, fallback to VTT or any)
                    string? subtitleUrl = null;
                    if (p.TryGetProperty("subtitles", out var subsProp) && subsProp.ValueKind == JsonValueKind.Array)
                    {
                        string? assUrl = null;
                        string? anyUrl = null;
                        foreach (var sub in subsProp.EnumerateArray())
                        {
                            var format = sub.TryGetProperty("format", out var fProp) ? fProp.GetString() : null;
                            var subSrc = sub.TryGetProperty("src", out var sProp) ? sProp.GetString() : null;
                            if (string.IsNullOrWhiteSpace(subSrc)) continue;

                            if (subSrc.StartsWith("//", StringComparison.Ordinal))
                                subSrc = $"{StreamingConstants.HttpsPrefix}{subSrc[2..]}";
                            else if (subSrc.StartsWith('/'))
                                subSrc = $"{StreamingConstants.AnimeLib.SiteUrl}{subSrc}";

                            var subUploadIdx = subSrc.IndexOf(StreamingConstants.AnimeLib.SubtitlesPathPrefix, StringComparison.OrdinalIgnoreCase);
                            if (subUploadIdx >= 0)
                            {
                                subSrc = $"{StreamingConstants.AnimeLib.SubtitlesServer}{subSrc[subUploadIdx..]}";
                            }
                            else if (subSrc.Contains("anilib.me", StringComparison.OrdinalIgnoreCase) ||
                                     subSrc.Contains("mangalib.me", StringComparison.OrdinalIgnoreCase))
                            {
                                subSrc = subSrc.Replace("api2.mangalib.me", "api.cdnlibs.org", StringComparison.OrdinalIgnoreCase)
                                               .Replace("api.anilib.me", "api.cdnlibs.org", StringComparison.OrdinalIgnoreCase)
                                               .Replace("mangalib.me", "cdnlibs.org", StringComparison.OrdinalIgnoreCase)
                                               .Replace("anilib.me", "cdnlibs.org", StringComparison.OrdinalIgnoreCase);
                            }

                            if (string.Equals(format, "ass", StringComparison.OrdinalIgnoreCase))
                                assUrl = subSrc;
                            anyUrl ??= subSrc;
                        }
                        subtitleUrl = assUrl ?? anyUrl;
                    }

                    foreach (var q in qualityProp.EnumerateArray())
                    {
                        if (!q.TryGetProperty("href", out var hrefProp))
                            continue;

                        var href = hrefProp.GetString();
                        if (string.IsNullOrWhiteSpace(href))
                            continue;

                        var quality = q.TryGetProperty("quality", out var qProp) ? qProp.GetInt32() : 1080;
                        var directUrl = baseServerUrl.TrimEnd('/') + "/" + href.TrimStart('/');
                        if (directUrl.Contains("anilib.me", StringComparison.OrdinalIgnoreCase))
                        {
                            directUrl = directUrl.Replace("video2.anilib.me", "video1.cdnlibs.org", StringComparison.OrdinalIgnoreCase)
                                                 .Replace("video3.anilib.me", "video1.cdnlibs.org", StringComparison.OrdinalIgnoreCase)
                                                 .Replace("anilib.me", "cdnlibs.org", StringComparison.OrdinalIgnoreCase);
                        }
                        var dubberName = !string.IsNullOrWhiteSpace(teamName) ? teamName : "AnimeLib";
                        var sourceTitle = !string.IsNullOrWhiteSpace(teamName)
                            ? $"{teamName} [AnimeLib MP4 {quality}p]"
                            : $"AnimeLib MP4 {quality}p";

                        sources.Add(new StreamingSource
                        {
                            Title = sourceTitle,
                            PlayerUrl = directUrl,
                            Dubber = dubberName,
                            Quality = $"{quality}p",
                            Provider = ProviderName,
                            SubtitleUrl = subtitleUrl
                        });
                    }
                    continue;
                }

                // Fallback to Kodik or embed player
                if (!p.TryGetProperty("src", out var srcProp))
                    continue;

                var src = srcProp.GetString();
                if (string.IsNullOrWhiteSpace(src))
                    continue;

                var dubber = !string.IsNullOrWhiteSpace(teamName) ? teamName : playerType;
                var title = !string.IsNullOrWhiteSpace(teamName)
                    ? $"{teamName} ({playerType})"
                    : playerType ?? "Player";

                var playerUrl = src.StartsWith("//") ? $"{StreamingConstants.HttpsPrefix}{src[2..]}" : src;

                sources.Add(new StreamingSource
                {
                    Title = title,
                    PlayerUrl = playerUrl,
                    Dubber = dubber,
                    Provider = ProviderName
                });
            }

            // Prioritize native AnimeLib MP4 streams if present, sorting higher resolution first
            sources.Sort((a, b) =>
            {
                bool aDirect = a.Title.Contains("AnimeLib MP4", StringComparison.OrdinalIgnoreCase);
                bool bDirect = b.Title.Contains("AnimeLib MP4", StringComparison.OrdinalIgnoreCase);
                if (aDirect != bDirect) return bDirect ? 1 : -1;

                if (aDirect && bDirect)
                {
                    int qA = ParseQuality(a.Quality);
                    int qB = ParseQuality(b.Quality);
                    if (qA != qB) return qB.CompareTo(qA);
                }

                return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
            });

            return sources;
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AnimeLib] Failed to load sources for ep {AnimeId}", animeId);
            return [];
        }
    }

    private void AddDefaultAnimeLibHeaders(HttpRequestMessage request, string? token = null)
    {
        request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Accept, StreamingConstants.Headers.JsonAccept);
        request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);
        request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Referer, StreamingConstants.AnimeLib.RefererUrl);
        request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Origin, StreamingConstants.AnimeLib.SiteUrl);
        request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.SiteId, StreamingConstants.Headers.SiteIdAnimeLib);

        var cleanToken = AnimeLibTokenHelper.CleanToken(token);
        if (!string.IsNullOrWhiteSpace(cleanToken))
        {
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Authorization, $"Bearer {cleanToken}");
        }
    }

    private static int ParseQuality(string? qualityStr)
    {
        if (string.IsNullOrWhiteSpace(qualityStr)) return 0;
        var digits = new string(qualityStr.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var val) ? val : 0;
    }

    private static string ResolveVideoServerUrl(string? domainId) => domainId switch
    {
        "secondary_1" => StreamingConstants.AnimeLib.VideoServerSecondary1,
        "secondary_2" => StreamingConstants.AnimeLib.VideoServerSecondary2,
        _ => StreamingConstants.AnimeLib.VideoServerMain
    };

    private static string ExtractAnimeId(string input)
    {
        var dashIndex = input.IndexOf("--", StringComparison.Ordinal);
        if (dashIndex > 0)
        {
            return input[..dashIndex];
        }
        return input;
    }
}
