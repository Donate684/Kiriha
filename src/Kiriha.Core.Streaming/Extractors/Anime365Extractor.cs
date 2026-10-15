using System.Text.Json;
using System.Text.RegularExpressions;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Models;
using Kiriha.Core.Streaming.Services;
using Serilog;

namespace Kiriha.Core.Streaming.Extractors;

/// <summary>
/// Video extractor for Anime 365 / Smotret Anime translation embeds (https://smotret-anime.org).
/// Resolves playable video streams and external ASS softsubtitles using authenticated session token.
/// </summary>
public sealed partial class Anime365Extractor : IVideoExtractor
{
    private static readonly Regex DomainRegex = GenerateDomainRegex();
    private static readonly Regex TranslationIdRegex = GenerateTranslationIdRegex();
    private static readonly Regex TokenRegex = GenerateTokenRegex();

    private readonly HttpClient _httpClient;
    private readonly ISettingsService? _settingsService;
    private readonly Anime365HostResolver? _hostResolver;

    public string Name => StreamingConstants.Providers.Anime365;

    public Anime365Extractor(
        HttpClient httpClient,
        ISettingsService? settingsService = null,
        Anime365HostResolver? hostResolver = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _settingsService = settingsService;
        _hostResolver = hostResolver;
    }

    public bool CanExtract(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return DomainRegex.IsMatch(url) || url.Contains(StreamingConstants.Anime365.EmbedPath, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<StreamingVideo>> ExtractVideosAsync(string playerUrl, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(playerUrl);

        try
        {
            var transIdMatch = TranslationIdRegex.Match(playerUrl);
            var transId = transIdMatch.Success ? transIdMatch.Groups[1].Value : null;

            var token = ExtractTokenFromUrl(playerUrl);
            if (string.IsNullOrWhiteSpace(token))
            {
                token = _settingsService?.Current.Torrents.OnlineStreaming.Anime365Token;
            }

            if (string.IsNullOrWhiteSpace(transId))
            {
                return [new StreamingVideo { Url = playerUrl, Quality = 1080, VideoType = DetectVideoType(playerUrl) }];
            }

            var webEmbedUrl = $"{StreamingConstants.Anime365.EmbedWebUrlPrefix}{transId}";
            if (_hostResolver != null)
            {
                webEmbedUrl = _hostResolver.Rewrite(webEmbedUrl);
            }
            if (!string.IsNullOrWhiteSpace(token))
            {
                webEmbedUrl += $"?access_token={Uri.EscapeDataString(token)}";
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                Log.Warning("[Anime365Extractor] No access token configured for {Url}", playerUrl);
                return [new StreamingVideo { Url = webEmbedUrl, Quality = 1080, VideoType = "embed" }];
            }

            var apiEmbedUrl = $"{StreamingConstants.Anime365.EmbedEndpoint}/{transId}?access_token={Uri.EscapeDataString(token)}";
            if (_hostResolver != null)
            {
                apiEmbedUrl = _hostResolver.Rewrite(apiEmbedUrl);
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, apiEmbedUrl);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Accept, "application/json");

            using var response = _hostResolver != null
                ? await _hostResolver.SendWithFailoverAsync(_httpClient, request, ct).ConfigureAwait(false)
                : await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("[Anime365Extractor] API embed returned {Status} for translation {Id}, falling back to web embed", response.StatusCode, transId);
                return [new StreamingVideo { Url = webEmbedUrl, Quality = 1080, VideoType = "embed" }];
            }

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("data", out var dataProp))
            {
                return [new StreamingVideo { Url = webEmbedUrl, Quality = 1080, VideoType = "embed" }];
            }

            string? subtitlesUrl = null;
            if (dataProp.TryGetProperty("subtitlesUrl", out var subProp) && subProp.ValueKind == JsonValueKind.String && subProp.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
            {
                subtitlesUrl = ResolveSubtitlesUrl(s);
            }
            else if (dataProp.TryGetProperty("subtitlesVttUrl", out var vttProp) && vttProp.ValueKind == JsonValueKind.String && vttProp.GetString() is { } vtt && !string.IsNullOrWhiteSpace(vtt))
            {
                subtitlesUrl = ResolveSubtitlesUrl(vtt);
            }
            else if (dataProp.TryGetProperty("subtitles", out var subsProp))
            {
                if (subsProp.ValueKind == JsonValueKind.String && subsProp.GetString() is { } subStr && !string.IsNullOrWhiteSpace(subStr))
                {
                    subtitlesUrl = ResolveSubtitlesUrl(subStr);
                }
            }

            var videos = ParseStreamsFromData(dataProp, subtitlesUrl);
            if (videos.Count > 0)
            {
                return videos;
            }

            Log.Warning("[Anime365Extractor] No direct video streams found in embed data for translation {Id}", transId);
            return [];
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Anime365Extractor] Failed to extract video from {Url}", playerUrl);
            return [];
        }
    }

    private string ResolveSubtitlesUrl(string subUrl)
    {
        if (string.IsNullOrWhiteSpace(subUrl)) return subUrl;
        if (subUrl.StartsWith('/'))
        {
            var host = _hostResolver?.ActiveMirror ?? StreamingConstants.Anime365.DefaultMirror;
            return $"{StreamingConstants.HttpsPrefix}{host}{subUrl}";
        }
        return _hostResolver?.Rewrite(subUrl) ?? subUrl;
    }

    public static IReadOnlyList<StreamingVideo> ParseStreamsFromData(JsonElement data, string? subtitlesUrl)
    {
        var list = new List<StreamingVideo>();

        if (data.TryGetProperty("stream", out var streamProp))
        {
            ParseStreamElement(streamProp, list, subtitlesUrl);
        }

        if (list.Count == 0 && data.TryGetProperty("download", out var downloadProp))
        {
            ParseStreamElement(downloadProp, list, subtitlesUrl);
        }

        if (list.Count == 0 && data.TryGetProperty("urls", out var urlsProp) && urlsProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in urlsProp.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() is { } url && !string.IsNullOrWhiteSpace(url))
                {
                    var quality = ParseQuality(prop.Name);
                    list.Add(new StreamingVideo
                    {
                        Url = url,
                        Quality = quality,
                        VideoType = DetectVideoType(url),
                        SubtitleUrl = subtitlesUrl
                    });
                }
            }
        }

        if (list.Count == 0)
        {
            string[] candidateKeys = ["streamUrl", "url", "videoUrl", "downloadUrl", "directUrl"];
            foreach (var key in candidateKeys)
            {
                if (data.TryGetProperty(key, out var uProp) && uProp.ValueKind == JsonValueKind.String && uProp.GetString() is { } u && !string.IsNullOrWhiteSpace(u))
                {
                    list.Add(new StreamingVideo
                    {
                        Url = u,
                        Quality = 1080,
                        VideoType = DetectVideoType(u),
                        SubtitleUrl = subtitlesUrl
                    });
                    break;
                }
            }
        }

        return list.OrderByDescending(v => v.Quality).ToList();
    }

    private static void ParseStreamElement(JsonElement prop, List<StreamingVideo> list, string? subtitlesUrl)
    {
        if (prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in prop.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var quality = 1080;
                    if (item.TryGetProperty("height", out var hProp))
                    {
                        if (hProp.ValueKind == JsonValueKind.Number && hProp.TryGetInt32(out var h))
                            quality = h;
                        else if (hProp.ValueKind == JsonValueKind.String && int.TryParse(hProp.GetString(), out var hs))
                            quality = hs;
                    }
                    else if (item.TryGetProperty("quality", out var qProp))
                    {
                        quality = ParseQuality(qProp.ToString());
                    }

                    if (item.TryGetProperty("urls", out var urlsArray) && urlsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var u in urlsArray.EnumerateArray())
                        {
                            if (u.ValueKind == JsonValueKind.String && u.GetString() is { } urlStr && !string.IsNullOrWhiteSpace(urlStr))
                            {
                                list.Add(new StreamingVideo
                                {
                                    Url = urlStr,
                                    Quality = quality,
                                    VideoType = DetectVideoType(urlStr),
                                    SubtitleUrl = subtitlesUrl
                                });
                                break;
                            }
                        }
                    }
                    else if (item.TryGetProperty("url", out var uSingle) && uSingle.ValueKind == JsonValueKind.String && uSingle.GetString() is { } singleUrl && !string.IsNullOrWhiteSpace(singleUrl))
                    {
                        list.Add(new StreamingVideo
                        {
                            Url = singleUrl,
                            Quality = quality,
                            VideoType = DetectVideoType(singleUrl),
                            SubtitleUrl = subtitlesUrl
                        });
                    }
                }
                else if (item.ValueKind == JsonValueKind.String && item.GetString() is { } directStr && !string.IsNullOrWhiteSpace(directStr))
                {
                    list.Add(new StreamingVideo
                    {
                        Url = directStr,
                        Quality = 1080,
                        VideoType = DetectVideoType(directStr),
                        SubtitleUrl = subtitlesUrl
                    });
                }
            }
        }
        else if (prop.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in prop.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { } url && !string.IsNullOrWhiteSpace(url))
                {
                    var quality = ParseQuality(p.Name);
                    list.Add(new StreamingVideo
                    {
                        Url = url,
                        Quality = quality,
                        VideoType = DetectVideoType(url),
                        SubtitleUrl = subtitlesUrl
                    });
                }
            }
        }
        else if (prop.ValueKind == JsonValueKind.String && prop.GetString() is { } str && !string.IsNullOrWhiteSpace(str))
        {
            list.Add(new StreamingVideo
            {
                Url = str,
                Quality = 1080,
                VideoType = DetectVideoType(str),
                SubtitleUrl = subtitlesUrl
            });
        }
    }

    private static string DetectVideoType(string url)
    {
        if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)) return "m3u8";
        if (url.Contains(".mpd", StringComparison.OrdinalIgnoreCase)) return "mpd";
        if (url.Contains(".mp4", StringComparison.OrdinalIgnoreCase)) return "mp4";
        return "mp4";
    }

    private static int ParseQuality(string name)
    {
        if (name.Contains("1080")) return 1080;
        if (name.Contains("720")) return 720;
        if (name.Contains("480")) return 480;
        if (name.Contains("360")) return 360;
        return 720;
    }

    private static string? ExtractTokenFromUrl(string url)
    {
        var match = TokenRegex.Match(url);
        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
    }

    [GeneratedRegex(@"smotret-?anime\.(?:org|app|co|com)|anime365\.(?:org|app|co|com)", RegexOptions.IgnoreCase)]
    private static partial Regex GenerateDomainRegex();

    [GeneratedRegex(@"(?:embed|translations)/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex GenerateTranslationIdRegex();

    [GeneratedRegex(@"[?&]access_token=([^&#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex GenerateTokenRegex();
}
