using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Tracking.Api;
using Serilog;

namespace Kiriha.Core.Tracking.Auth;

public partial class ShikiAuthService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly ShikiHostResolver _hostResolver;
    private readonly ILocalizer _localizer;

    private ShikiMirror ActiveMirror => _settingsService.Current.Api.ShikiMirror;

    public ShikiAuthService(
        HttpClient httpClient,
        ISettingsService settingsService,
        ShikiHostResolver hostResolver,
        ILocalizer? localizer = null)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _hostResolver = hostResolver;
        _localizer = localizer ?? ILocalizer.Passthrough;
    }

    public string GetAuthUrl(ShikiMirror? mirror = null)
    {
        var targetMirror = mirror ?? ActiveMirror;
        // Shikimori redirect URI must match exactly what's in the application settings on Shikimori website
        return $"{ShikiEndpoints.AuthUrl(targetMirror)}?client_id={ShikiEndpoints.ClientId(targetMirror)}&redirect_uri={AppConstants.Api.RedirectUri}&response_type=code&scope=user_rates";
    }

    public async Task<ShikiTokens?> LoginAsync(ShikiMirror? mirror = null, CancellationToken cancellationToken = default)
    {
        var targetMirror = mirror ?? ActiveMirror;
        if (!ShikiEndpoints.IsConfigured(targetMirror))
        {
            Log.Error("Shikimori OAuth is not configured for mirror {Mirror}. Set ClientId/TokenUrl first.", targetMirror);
            return null;
        }

        var authUrl = GetAuthUrl(targetMirror);
        string successMessage = _localizer.GetLoc("auth.success", "Shikimori");
        string closeMessage = _localizer.GetLoc("auth.close_window");
        var code = await OAuthHelper.AuthorizeViaLoopbackAsync(authUrl, AppConstants.Api.RedirectUri, successMessage, closeMessage, cancellationToken);

        if (string.IsNullOrEmpty(code) || cancellationToken.IsCancellationRequested) return null;

        var tokens = await ExchangeCodeForTokenAsync(code, targetMirror);
        if (tokens != null)
        {
            tokens.Mirror = targetMirror;
            using var request = new HttpRequestMessage(HttpMethod.Get, ShikiEndpoints.BaseUrl(targetMirror) + ShikiEndpoints.CurrentUserPath);
            request.Headers.Add("User-Agent", AppInfo.UserAgent);
            tokens.UserId = await TrackerAuthProfile.ReadUserIdAsync(
                request, tokens.AccessToken, ct => ShikiHttp.SendShikiAsync(_httpClient, request, _hostResolver, ct),
                cancellationToken);
        }
        return tokens;
    }


}
