using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Constants;
using Kiriha.Core.Streaming.Models;
using Serilog;

namespace Kiriha.Core.Streaming.Extractors;

/// <summary>
/// Resolves direct HLS streams from Kodik embed player URLs using ROT-18 cipher and dynamic API endpoints.
/// </summary>
public sealed partial class KodikVideoExtractor : IVideoExtractor
{
    private static readonly Regex UrlRegex = GenerateUrlRegex();
    private static readonly Regex DomainRegex = GenerateDomainRegex();
    private static readonly Regex DSignRegex = GenerateDSignRegex();
    private static readonly Regex PdRegex = GeneratePdRegex();
    private static readonly Regex PdSignRegex = GeneratePdSignRegex();
    private static readonly Regex RefRegex = GenerateRefRegex();
    private static readonly Regex RefSignRegex = GenerateRefSignRegex();
    private static readonly Regex TypeRegex = GenerateTypeRegex();
    private static readonly Regex HashRegex = GenerateHashRegex();
    private static readonly Regex IdRegex = GenerateIdRegex();
    private static readonly Regex ScriptJsRegex = GenerateScriptJsRegex();
    private static readonly Regex ApiPathRegex = GenerateApiPathRegex();

    private readonly HttpClient _httpClient;
    private static string? _cachedApiPath;

    public string Name => "Kodik";

    public KodikVideoExtractor(HttpClient httpClient)
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
            var uri = new Uri(playerUrl);
            var netloc = uri.Host;

            using var pageRequest = new HttpRequestMessage(HttpMethod.Get, playerUrl);
            pageRequest.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);

            using var pageResponse = await _httpClient.SendAsync(pageRequest, ct).ConfigureAwait(false);
            if (!pageResponse.IsSuccessStatusCode)
                return [];

            var pageHtml = await pageResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (pageHtml.Contains("\u0412\u0438\u0434\u0435\u043e \u043d\u0435 \u043d\u0430\u0439\u0434\u0435\u043d\u043e") || pageHtml.Contains("An unhandled lowlevel error occurred"))
                return [];

            var payload = ExtractApiPayload(pageHtml);
            if (payload == null)
            {
                Log.Warning("[Kodik] Failed to extract API payload from {Url}", playerUrl);
                return [];
            }

            var scriptPath = ScriptJsRegex.Match(pageHtml).Groups[1].Value;

            if (string.IsNullOrEmpty(_cachedApiPath) && !string.IsNullOrEmpty(scriptPath))
            {
                var playerJsUrl = $"{StreamingConstants.HttpsPrefix}{netloc}{(scriptPath.StartsWith('/') ? scriptPath : "/" + scriptPath)}";
                await UpdateCachedApiPathAsync(playerJsUrl, ct).ConfigureAwait(false);
            }

            var apiPath = _cachedApiPath ?? "/ftor";
            var apiUrl = $"{StreamingConstants.HttpsPrefix}{netloc}{apiPath}";

            var videos = await CallKodikApiAsync(apiUrl, payload, playerUrl, netloc, ct).ConfigureAwait(false);
            if (videos.Count > 0)
                return videos;

            // If initial call failed, re-fetch API path and retry once
            if (!string.IsNullOrEmpty(scriptPath))
            {
                var playerJsUrl = $"{StreamingConstants.HttpsPrefix}{netloc}{(scriptPath.StartsWith('/') ? scriptPath : "/" + scriptPath)}";
                await UpdateCachedApiPathAsync(playerJsUrl, ct).ConfigureAwait(false);
                apiPath = _cachedApiPath ?? "/ftor";
                apiUrl = $"{StreamingConstants.HttpsPrefix}{netloc}{apiPath}";
                return await CallKodikApiAsync(apiUrl, payload, playerUrl, netloc, ct).ConfigureAwait(false);
            }

            return [];
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Kodik] Error extracting videos from {Url}", playerUrl);
            return [];
        }
    }

    private async Task<IReadOnlyList<StreamingVideo>> CallKodikApiAsync(
        string apiUrl,
        Dictionary<string, string> payload,
        string playerUrl,
        string netloc,
        CancellationToken ct)
    {
        using var apiRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl);
        apiRequest.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Origin, $"{StreamingConstants.HttpsPrefix}{netloc}");
        apiRequest.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Referer, playerUrl);
        apiRequest.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Accept, StreamingConstants.Headers.JsonAccept);
        apiRequest.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);

        apiRequest.Content = new FormUrlEncodedContent(payload);

        using var response = await _httpClient.SendAsync(apiRequest, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return [];

        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (content.Contains("promo-error-box") || content.Contains("\u041e\u0448\u0438\u0431\u043a\u0430 \u043c\u0430\u043d\u0438\u0444\u0435\u0441\u0442\u0430"))
            return [];

        using var doc = JsonDocument.Parse(content);
        if (!doc.RootElement.TryGetProperty("links", out var linksProp))
            return [];

        var headers = new Dictionary<string, string>
        {
            [StreamingConstants.Headers.Referer] = playerUrl,
            [StreamingConstants.Headers.Origin] = $"{StreamingConstants.HttpsPrefix}{netloc}",
            [StreamingConstants.Headers.UserAgent] = StreamingConstants.Headers.DefaultUserAgent
        };

        var result = new List<StreamingVideo>();

        if (linksProp.TryGetProperty("720", out var p720) && p720.GetArrayLength() > 0)
        {
            var raw = p720[0].GetProperty("src").GetString();
            if (!string.IsNullOrEmpty(raw))
            {
                var decoded = DecodeUrl(raw).Replace("/480.mp4:", "/720.mp4:");
                result.Add(new StreamingVideo { Url = decoded, Quality = 720, VideoType = "m3u8", Headers = headers });
            }
        }

        if (linksProp.TryGetProperty("480", out var p480) && p480.GetArrayLength() > 0)
        {
            var raw = p480[0].GetProperty("src").GetString();
            if (!string.IsNullOrEmpty(raw))
            {
                var decoded = DecodeUrl(raw);
                result.Add(new StreamingVideo { Url = decoded, Quality = 480, VideoType = "m3u8", Headers = headers });
            }
        }

        if (linksProp.TryGetProperty("360", out var p360) && p360.GetArrayLength() > 0)
        {
            var raw = p360[0].GetProperty("src").GetString();
            if (!string.IsNullOrEmpty(raw))
            {
                var decoded = DecodeUrl(raw);
                result.Add(new StreamingVideo { Url = decoded, Quality = 360, VideoType = "m3u8", Headers = headers });
            }
        }

        return result;
    }

    private async Task UpdateCachedApiPathAsync(string playerJsUrl, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, playerJsUrl);
            req.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);
            using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return;

            var js = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var m = ApiPathRegex.Match(js);
            if (m.Success)
            {
                var b64 = m.Groups[1].Value;
                var bytes = Convert.FromBase64String(PadBase64(b64));
                _cachedApiPath = Encoding.UTF8.GetString(bytes);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Kodik] Could not update API path from JS {JsUrl}", playerJsUrl);
        }
    }

    private static Dictionary<string, string>? ExtractApiPayload(string html)
    {
        var d = DomainRegex.Match(html).Groups[1].Value;
        var dSign = DSignRegex.Match(html).Groups[1].Value;
        var pd = PdRegex.Match(html).Groups[1].Value;
        var pdSign = PdSignRegex.Match(html).Groups[1].Value;
        var refVal = RefRegex.Match(html).Groups[1].Value;
        var refSign = RefSignRegex.Match(html).Groups[1].Value;
        var type = TypeRegex.Match(html).Groups[1].Value;
        var hash = HashRegex.Match(html).Groups[1].Value;
        var id = IdRegex.Match(html).Groups[1].Value;

        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(id))
            return null;

        return new Dictionary<string, string>
        {
            ["d"] = d,
            ["d_sign"] = dSign,
            ["pd"] = pd,
            ["pd_sign"] = pdSign,
            ["ref"] = refVal,
            ["ref_sign"] = refSign,
            ["type"] = type,
            ["hash"] = hash,
            ["id"] = id,
            ["bad_user"] = "false",
            ["cdn_is_working"] = "true",
            ["info"] = "{}"
        };
    }

    /// <summary>
    /// Decrypts a ROT-18 + Base64 encoded Kodik video URL.
    /// </summary>
    public static string DecodeUrl(string encodedUrl)
    {
        ArgumentNullException.ThrowIfNull(encodedUrl);

        if (encodedUrl.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            return encodedUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase)
                ? encodedUrl
                : $"https:{encodedUrl}";
        }

        var decryptedRot = DecryptRot18(encodedUrl);
        var base64Padded = PadBase64(decryptedRot);

        var bytes = Convert.FromBase64String(base64Padded);
        var decoded = Encoding.UTF8.GetString(bytes);

        return decoded.StartsWith("https", StringComparison.OrdinalIgnoreCase)
            ? decoded
            : $"https:{decoded}";
    }

    /// <summary>
    /// Applies a ROT-18 (ROT-13 + 5) cipher transformation to the input string.
    /// </summary>
    public static string DecryptRot18(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (char.IsAsciiLetterUpper(ch))
            {
                sb.Append((char)((ch - 'A' + 18) % 26 + 'A'));
            }
            else if (char.IsAsciiLetterLower(ch))
            {
                sb.Append((char)((ch - 'a' + 18) % 26 + 'a'));
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    private static string PadBase64(string input)
    {
        var mod = input.Length % 4;
        if (mod > 0)
        {
            return input + new string('=', 4 - mod);
        }
        return input;
    }

    [GeneratedRegex(@"https?://(?:www\.)?[\w-]+\.\w+/(?:serial|seria|season|video|film)/\d+/\w+/\d{3,4}p", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex GenerateUrlRegex();

    [GeneratedRegex(@"var\s*domain\s*=\s*['""](.*?)['""];", RegexOptions.Compiled)]
    private static partial Regex GenerateDomainRegex();

    [GeneratedRegex(@"var\s*d_sign\s*=\s*['""](.*?)['""];", RegexOptions.Compiled)]
    private static partial Regex GenerateDSignRegex();

    [GeneratedRegex(@"var\s*pd\s*=\s*['""](.*?)['""];", RegexOptions.Compiled)]
    private static partial Regex GeneratePdRegex();

    [GeneratedRegex(@"var\s*pd_sign\s*=\s*['""](.*?)['""];", RegexOptions.Compiled)]
    private static partial Regex GeneratePdSignRegex();

    [GeneratedRegex(@"var\s*ref\s*=\s*['""](.*?)['""];", RegexOptions.Compiled)]
    private static partial Regex GenerateRefRegex();

    [GeneratedRegex(@"var\s*ref_sign\s*=\s*['""](.*?)['""];", RegexOptions.Compiled)]
    private static partial Regex GenerateRefSignRegex();

    [GeneratedRegex(@"vInfo\.type\s*=\s*['""](.*?)['""];", RegexOptions.Compiled)]
    private static partial Regex GenerateTypeRegex();

    [GeneratedRegex(@"vInfo\.hash\s*=\s*['""](.*?)['""];", RegexOptions.Compiled)]
    private static partial Regex GenerateHashRegex();

    [GeneratedRegex(@"vInfo\.id\s*=\s*['""](.*?)['""];", RegexOptions.Compiled)]
    private static partial Regex GenerateIdRegex();

    [GeneratedRegex(@"<script[^>]*src\s*=\s*[""']([^""']*assets/js[^""']*)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex GenerateScriptJsRegex();

    [GeneratedRegex(@"\$\.ajax[^)]+atob\([""'](\w+=*)[""']\)", RegexOptions.Compiled)]
    private static partial Regex GenerateApiPathRegex();
}
