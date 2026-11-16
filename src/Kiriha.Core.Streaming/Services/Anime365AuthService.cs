using System.Text.Json;
using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Constants;
using Serilog;

namespace Kiriha.Core.Streaming.Services;

/// <summary>
/// Handles Anime 365 authentication and account/premium status verification
/// through pooled <see cref="HttpClient"/> instances and mirror failover.
/// </summary>
public sealed class Anime365AuthService : IAnime365AuthService
{
    private const string DefaultAuthError = "Authentication failed";

    private readonly HttpClient _httpClient;
    private readonly Anime365HostResolver? _hostResolver;

    public Anime365AuthService(HttpClient httpClient, Anime365HostResolver? hostResolver = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _hostResolver = hostResolver;
    }

    public async Task<Anime365LoginResult> LoginAsync(
        string email,
        string password,
        string? appId = null,
        CancellationToken ct = default)
    {
        var effectiveAppId = string.IsNullOrWhiteSpace(appId)
            ? StreamingConstants.Anime365.DefaultAppId
            : appId;

        var loginUrl = $"{StreamingConstants.Anime365.LoginEndpoint}?app={Uri.EscapeDataString(effectiveAppId)}&email={Uri.EscapeDataString(email.Trim())}&password={Uri.EscapeDataString(password)}";
        if (_hostResolver != null)
        {
            loginUrl = _hostResolver.Rewrite(loginUrl);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, loginUrl);
        request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);
        request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Accept, StreamingConstants.Headers.JsonMediaType);

        using var response = _hostResolver != null
            ? await _hostResolver.SendWithFailoverAsync(_httpClient, request, ct).ConfigureAwait(false)
            : await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(content);

        if (doc.RootElement.TryGetProperty("error", out var errorProp))
        {
            var msg = errorProp.TryGetProperty("message", out var mProp)
                ? mProp.GetString()
                : DefaultAuthError;
            return new Anime365LoginResult(false, null, msg ?? DefaultAuthError);
        }

        if (!doc.RootElement.TryGetProperty("data", out var dataProp) ||
            !dataProp.TryGetProperty("access_token", out var tokenProp) ||
            tokenProp.GetString() is not { } token ||
            string.IsNullOrWhiteSpace(token))
        {
            return new Anime365LoginResult(false, null, null, TokenMissing: true);
        }

        return new Anime365LoginResult(true, token.Trim(), null);
    }

    public async Task<Anime365AccountStatus?> GetAccountStatusAsync(
        string token,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var meUrl = $"{StreamingConstants.Anime365.MeEndpoint}?access_token={Uri.EscapeDataString(token)}";
            if (_hostResolver != null)
            {
                meUrl = _hostResolver.Rewrite(meUrl);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, meUrl);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.UserAgent, StreamingConstants.Headers.DefaultUserAgent);
            request.Headers.TryAddWithoutValidation(StreamingConstants.Headers.Accept, StreamingConstants.Headers.JsonMediaType);

            using var response = _hostResolver != null
                ? await _hostResolver.SendWithFailoverAsync(_httpClient, request, ct).ConfigureAwait(false)
                : await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("data", out var dataProp))
                return null;

            var isLoggedIn = dataProp.TryGetProperty("isLogined", out var logProp) && logProp.GetBoolean();
            if (!isLoggedIn)
                return new Anime365AccountStatus(false, string.Empty, false, null);

            var username = dataProp.TryGetProperty("name", out var nameProp) && nameProp.GetString() is { Length: > 0 } name
                ? name
                : StreamingConstants.Anime365.DefaultUsername;
            var isPremium = dataProp.TryGetProperty("isPremium", out var premProp) && premProp.GetBoolean();
            var premiumUntil = dataProp.TryGetProperty("premiumUntil", out var untilProp) ? untilProp.GetString() : null;

            return new Anime365AccountStatus(true, username, isPremium, premiumUntil);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Anime365Auth] Failed to query Anime365 account status");
            return null;
        }
    }
}
