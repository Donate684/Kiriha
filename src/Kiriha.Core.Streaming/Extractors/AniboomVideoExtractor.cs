using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Models;
using Serilog;

namespace Kiriha.Core.Streaming.Extractors;

/// <summary>
/// Resolves HLS and DASH streams from Aniboom embed player URLs.
/// </summary>
public sealed partial class AniboomVideoExtractor : IVideoExtractor
{
    private static readonly Regex UrlRegex = GenerateUrlRegex();
    private static readonly Regex DataParametersRegex = GenerateDataParametersRegex();
    private static readonly Regex HlsSrcRegex = GenerateHlsSrcRegex();
    private static readonly Regex DashSrcRegex = GenerateDashSrcRegex();

    private readonly HttpClient _httpClient;

    public string Name => "Aniboom";

    public AniboomVideoExtractor(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public bool CanExtract(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return UrlRegex.IsMatch(url);
    }

    public async Task<IReadOnlyList<StreamingVideo>> ExtractVideosAsync(string playerUrl, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(playerUrl);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, playerUrl);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Referer, StreamingConstants.Aniboom.AnimeGoReferer);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.AcceptLanguage, StreamingConstants.Headers.RuRuLanguage);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return [];

            var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseStreamsFromHtml(html);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Aniboom] Error extracting videos from {Url}", playerUrl);
            return [];
        }
    }

    /// <summary>
    /// Parses HLS/DASH streams from Aniboom page HTML.
    /// </summary>
    public static IReadOnlyList<StreamingVideo> ParseStreamsFromHtml(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var dataParamMatch = DataParametersRegex.Match(html);
        if (!dataParamMatch.Success)
            return [];

        var rawJson = WebUtility.HtmlDecode(dataParamMatch.Groups[1].Value);

        string? hlsUrl = null;
        string? dashUrl = null;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("hls", out var hlsProp))
            {
                if (hlsProp.ValueKind == JsonValueKind.String)
                {
                    var hlsStr = hlsProp.GetString();
                    if (!string.IsNullOrEmpty(hlsStr))
                    {
                        if (hlsStr.TrimStart().StartsWith('{'))
                        {
                            using var innerDoc = JsonDocument.Parse(hlsStr);
                            if (innerDoc.RootElement.TryGetProperty("src", out var srcProp))
                                hlsUrl = srcProp.GetString();
                        }
                        else
                        {
                            hlsUrl = hlsStr;
                        }
                    }
                }
                else if (hlsProp.ValueKind == JsonValueKind.Object && hlsProp.TryGetProperty("src", out var srcProp))
                {
                    hlsUrl = srcProp.GetString();
                }
            }

            if (root.TryGetProperty("dash", out var dashProp))
            {
                if (dashProp.ValueKind == JsonValueKind.String)
                {
                    var dashStr = dashProp.GetString();
                    if (!string.IsNullOrEmpty(dashStr))
                    {
                        if (dashStr.TrimStart().StartsWith('{'))
                        {
                            using var innerDoc = JsonDocument.Parse(dashStr);
                            if (innerDoc.RootElement.TryGetProperty("src", out var srcProp))
                                dashUrl = srcProp.GetString();
                        }
                        else
                        {
                            dashUrl = dashStr;
                        }
                    }
                }
                else if (dashProp.ValueKind == JsonValueKind.Object && dashProp.TryGetProperty("src", out var srcProp))
                {
                    dashUrl = srcProp.GetString();
                }
            }
        }
        catch
        {
            // Fallback to regex extraction if JSON is malformed
            var hlsMatch = HlsSrcRegex.Match(rawJson);
            if (hlsMatch.Success)
                hlsUrl = hlsMatch.Groups[1].Value.Replace(@"\/", "/");

            var dashMatch = DashSrcRegex.Match(rawJson);
            if (dashMatch.Success)
                dashUrl = dashMatch.Groups[1].Value.Replace(@"\/", "/");
        }

        var headers = new Dictionary<string, string>
        {
            [StreamingConstants.Headers.Referer] = StreamingConstants.Aniboom.RefererUrl,
            [StreamingConstants.Headers.Origin] = StreamingConstants.Aniboom.BaseUrl,
            [StreamingConstants.Headers.AcceptLanguage] = StreamingConstants.Headers.RuRuLanguage,
            [StreamingConstants.Headers.UserAgent] = StreamingConstants.Headers.DefaultUserAgent
        };

        var result = new List<StreamingVideo>();

        if (!string.IsNullOrEmpty(hlsUrl))
        {
            result.Add(new StreamingVideo
            {
                Url = hlsUrl,
                Quality = 1080,
                VideoType = "m3u8",
                Headers = headers
            });
        }

        if (!string.IsNullOrEmpty(dashUrl) && !dashUrl.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            result.Add(new StreamingVideo
            {
                Url = dashUrl,
                Quality = 1080,
                VideoType = "mpd",
                Headers = headers
            });
        }

        return result;
    }

    [GeneratedRegex(@"https?://(?:www\.)?aniboom\.one/", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex GenerateUrlRegex();

    [GeneratedRegex(@"data-parameters\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled)]
    private static partial Regex GenerateDataParametersRegex();

    [GeneratedRegex(@"""hls""\s*:\s*""?\{\s*\\?""src\\?""\s*:\s*\\?""([^""\\]+)""", RegexOptions.Compiled)]
    private static partial Regex GenerateHlsSrcRegex();

    [GeneratedRegex(@"""dash""\s*:\s*""?\{\s*\\?""src\\?""\s*:\s*\\?""([^""\\]+)""", RegexOptions.Compiled)]
    private static partial Regex GenerateDashSrcRegex();
}
