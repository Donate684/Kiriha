using System.Net;
using System.Text.RegularExpressions;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Streaming.Constants;
using Serilog;

namespace Kiriha.Core.Streaming.Services;

/// <summary>
/// Dynamic host resolver, mirror selector, and image rewriter for Anime 365 (smotret-anime).
/// Provides automatic failover across official mirrors:
/// smotret-anime.app (RU / no VPN), smotret-anime.co (Primary), smotret-anime.org, smotretanime.co, smotret-anime.com.
/// </summary>
public sealed partial class Anime365HostResolver : IImageUrlRewriter
{
    private static readonly Regex HostPattern = GenerateHostPattern();

    private readonly Lock _gate = new();
    private readonly ISettingsService? _settingsService;
    private string _activeMirror = StreamingConstants.Anime365.DefaultMirror;

    public Anime365HostResolver(ISettingsService? settingsService = null)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Gets the current active mirror host (e.g. "smotret-anime.app" or "smotret-anime.co").
    /// Prioritizes user's explicit preference from settings if configured.
    /// </summary>
    public string ActiveMirror
    {
        get
        {
            var userMirror = _settingsService?.Current.Torrents.OnlineStreaming.Anime365Mirror;
            if (!string.IsNullOrWhiteSpace(userMirror) &&
                !string.Equals(userMirror, "auto", StringComparison.OrdinalIgnoreCase) &&
                IsAnime365Host(userMirror))
            {
                return userMirror.Trim();
            }

            lock (_gate)
            {
                return _activeMirror;
            }
        }
    }

    public static bool IsAnime365Host(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        return HostPattern.IsMatch(host);
    }

    public string Rewrite(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        if (!IsAnime365Host(uri.Host)) return url;

        var targetHost = ActiveMirror;
        if (string.Equals(uri.Host, targetHost, StringComparison.OrdinalIgnoreCase))
            return url;

        return new UriBuilder(uri) { Host = targetHost }.Uri.ToString();
    }

    public Uri Rewrite(Uri original)
    {
        if (original == null) return null!;
        if (!IsAnime365Host(original.Host)) return original;

        var targetHost = ActiveMirror;
        if (string.Equals(original.Host, targetHost, StringComparison.OrdinalIgnoreCase))
            return original;

        return new UriBuilder(original) { Host = targetHost }.Uri;
    }

    public void RememberActiveMirror(string mirror)
    {
        if (string.IsNullOrWhiteSpace(mirror) || !IsAnime365Host(mirror)) return;

        lock (_gate)
        {
            if (!string.Equals(_activeMirror, mirror, StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("[Anime365] Switched active mirror from {Old} to {New}", _activeMirror, mirror);
                _activeMirror = mirror;
            }
        }
    }

    public IEnumerable<string> ProbeOrder(string? excluding = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var preferred = _settingsService?.Current.Torrents.OnlineStreaming.Anime365Mirror;
        if (!string.IsNullOrWhiteSpace(preferred) &&
            !string.Equals(preferred, "auto", StringComparison.OrdinalIgnoreCase) &&
            IsAnime365Host(preferred))
        {
            if (excluding == null || !string.Equals(preferred, excluding, StringComparison.OrdinalIgnoreCase))
            {
                seen.Add(preferred);
                yield return preferred;
            }
        }

        string current;
        lock (_gate)
        {
            current = _activeMirror;
        }

        if (seen.Add(current) && (excluding == null || !string.Equals(current, excluding, StringComparison.OrdinalIgnoreCase)))
        {
            yield return current;
        }

        foreach (var mirror in StreamingConstants.Anime365.KnownMirrors)
        {
            if (excluding != null && string.Equals(mirror, excluding, StringComparison.OrdinalIgnoreCase))
                continue;

            if (seen.Add(mirror))
            {
                yield return mirror;
            }
        }
    }

    /// <summary>
    /// Executes HTTP request against the active mirror with automatic failover to remaining mirrors on network/host failures.
    /// </summary>
    public async Task<HttpResponseMessage> SendWithFailoverAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);

        byte[]? contentBytes = null;
        if (request.Content != null)
        {
            contentBytes = await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }

        var initialUri = Rewrite(request.RequestUri!);
        var triedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var firstAttempt = CloneRequest(request, initialUri, contentBytes);
        triedHosts.Add(initialUri.Host);

        try
        {
            var response = await client.SendAsync(firstAttempt, ct).ConfigureAwait(false);
            if (IsSuccessfulOrGenuineApi(response.StatusCode))
            {
                RememberActiveMirror(initialUri.Host);
                return response;
            }

            response.Dispose();
        }
        catch (Exception ex) when (IsTransientConnectionException(ex, ct))
        {
            Log.Warning("[Anime365] Mirror {Host} unreachable: {Message}. Probing mirrors...", initialUri.Host, ex.Message);
        }

        // Failover: probe remaining mirrors
        foreach (var candidateHost in ProbeOrder(initialUri.Host))
        {
            if (!triedHosts.Add(candidateHost)) continue;

            var targetUri = new UriBuilder(initialUri) { Host = candidateHost }.Uri;
            Log.Information("[Anime365] Probing fallback mirror: {Host}", candidateHost);

            try
            {
                using var probeRequest = CloneRequest(request, targetUri, contentBytes);
                var probeResponse = await client.SendAsync(probeRequest, ct).ConfigureAwait(false);

                if (IsSuccessfulOrGenuineApi(probeResponse.StatusCode))
                {
                    RememberActiveMirror(candidateHost);
                    return probeResponse;
                }

                probeResponse.Dispose();
            }
            catch (Exception probeEx) when (IsTransientConnectionException(probeEx, ct))
            {
                // Continue to next mirror
            }
        }

        // If all candidate mirrors failed, do a final direct attempt with the original request to preserve natural exception
        using var finalRequest = CloneRequest(request, initialUri, contentBytes);
        return await client.SendAsync(finalRequest, ct).ConfigureAwait(false);
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage original, Uri targetUri, byte[]? contentBytes)
    {
        var clone = new HttpRequestMessage(original.Method, targetUri)
        {
            Version = original.Version
        };

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (contentBytes != null && original.Content != null)
        {
            var newContent = new ByteArrayContent(contentBytes);
            foreach (var h in original.Content.Headers)
            {
                newContent.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            clone.Content = newContent;
        }

        return clone;
    }

    private static bool IsSuccessfulOrGenuineApi(HttpStatusCode code)
    {
        var intCode = (int)code;
        // 2xx, 3xx are valid. 400, 401, 403, 404, 422 are genuine API responses (e.g. wrong password or not found)
        // 500, 502, 503, 504 are server/gateway failures where fallback to another mirror is appropriate
        return intCode < 500 && code != HttpStatusCode.BadGateway && code != HttpStatusCode.ServiceUnavailable && code != HttpStatusCode.GatewayTimeout;
    }

    private static bool IsTransientConnectionException(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        return ex is HttpRequestException || ex is TaskCanceledException || ex is TimeoutException;
    }

    [GeneratedRegex(@"^(?:smotret-?anime\.(?:co|com|org|app)|anime365\.(?:co|com|org|app))$", RegexOptions.IgnoreCase)]
    private static partial Regex GenerateHostPattern();
}
