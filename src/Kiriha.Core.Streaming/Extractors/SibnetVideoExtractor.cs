using System.Text.RegularExpressions;
using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Models;
using Serilog;

namespace Kiriha.Core.Streaming.Extractors;

/// <summary>
/// Resolves direct MP4 streams from Sibnet embed player URLs.
/// </summary>
public sealed partial class SibnetVideoExtractor : IVideoExtractor
{
    private static readonly Regex UrlRegex = GenerateUrlRegex();
    private static readonly Regex Mp4PathRegex = GenerateMp4PathRegex();

    private readonly HttpClient _httpClient;

    public string Name => "Sibnet";

    public SibnetVideoExtractor(HttpClient httpClient)
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
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Referer, playerUrl);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return [];

            var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseStreamsFromHtml(html, playerUrl);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Sibnet] Error extracting videos from {Url}", playerUrl);
            return [];
        }
    }

    /// <summary>
    /// Parses MP4 streams from Sibnet page HTML.
    /// </summary>
    public static IReadOnlyList<StreamingVideo> ParseStreamsFromHtml(string html, string playerUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(playerUrl);

        var match = Mp4PathRegex.Match(html);
        if (!match.Success)
            return [];

        var relativePath = match.Groups[1].Value;
        var directUrl = $"{StreamingConstants.Sibnet.BaseUrl}{relativePath}";

        var headers = new Dictionary<string, string>
        {
            [StreamingConstants.Headers.Referer] = playerUrl,
            [StreamingConstants.Headers.UserAgent] = StreamingConstants.Headers.DefaultUserAgent
        };

        return [
            new StreamingVideo
            {
                Url = directUrl,
                Quality = 480,
                VideoType = "mp4",
                Headers = headers
            }
        ];
    }

    [GeneratedRegex(@"https?://(?:www\.)?video\.sibnet\.ru/", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex GenerateUrlRegex();

    [GeneratedRegex(@"""(/v/[^""]*\.mp4)""", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex GenerateMp4PathRegex();
}
