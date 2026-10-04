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

    private ShikiMirror ActiveMirror => _settingsService.Current.Api.ShikiMirror;

    public ShikiAuthService(HttpClient httpClient, ISettingsService settingsService, ShikiHostResolver hostResolver)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _hostResolver = hostResolver;
    }

    public string GetAuthUrl(ShikiMirror? mirror = null)
    {
        var targetMirror = mirror ?? ActiveMirror;
        // Shikimori redirect URI must match exactly what's in the application settings on Shikimori website
        return $"{ShikiEndpoints.AuthUrl(targetMirror)}?client_id={ShikiEndpoints.ClientId(targetMirror)}&redirect_uri={AppConstants.Api.RedirectUri}&response_type=code&scope=user_rates";
    }

    public async Task<ShikiTokens?> LoginAsync(ShikiMirror? mirror = null)
    {
        var targetMirror = mirror ?? ActiveMirror;
        if (!ShikiEndpoints.IsConfigured(targetMirror))
        {
            Log.Error("Shikimori OAuth is not configured for mirror {Mirror}. Set ClientId/TokenUrl first.", targetMirror);
            return null;
        }

        var authUrl = GetAuthUrl(targetMirror);
        string successMessage = UIUtils.GetLoc("auth.success", "Shikimori");
        string closeMessage = UIUtils.GetLoc("auth.close_window");
        var code = await OAuthHelper.AuthorizeViaLoopbackAsync(authUrl, AppConstants.Api.RedirectUri, successMessage, closeMessage);

        if (string.IsNullOrEmpty(code)) return null;

        var tokens = await ExchangeCodeForTokenAsync(code, targetMirror);
        if (tokens != null) tokens.Mirror = targetMirror;
        return tokens;
    }


}
