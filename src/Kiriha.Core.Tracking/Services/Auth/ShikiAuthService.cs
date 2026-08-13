using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Shared.Shiki;
using Kiriha.Core.Tracking.Api;

using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Serilog;

namespace Kiriha.Core.Tracking.Auth;

public partial class ShikiAuthService
{
    private readonly HttpClient _httpClient;
    private readonly Kiriha.Core.Abstractions.Services.ISettingsService _settingsService;
    private readonly ShikiHostResolver _hostResolver;

    private ShikiMirror ActiveMirror => _settingsService.Current.Api.ShikiMirror;
    private string ClientId => ShikiEndpoints.ClientId(ActiveMirror);
    private string TokenUrl => ShikiEndpoints.TokenUrl(ActiveMirror);
    private string AuthBase => ShikiEndpoints.AuthUrl(ActiveMirror);

    public ShikiAuthService(HttpClient httpClient, Kiriha.Core.Abstractions.Services.ISettingsService settingsService, ShikiHostResolver hostResolver)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _hostResolver = hostResolver;
    }

    public string GetAuthUrl()
    {
        // Shikimori redirect URI must match exactly what's in the application settings on Shikimori website
        return $"{AuthBase}?client_id={ClientId}&redirect_uri={AppConstants.Api.RedirectUri}&response_type=code&scope=user_rates";
    }

    public async Task<ShikiTokens?> LoginAsync()
    {
        if (!ShikiEndpoints.IsConfigured(ActiveMirror))
        {
            Log.Error("Shikimori OAuth is not configured for mirror {Mirror}. Set ClientId/TokenUrl first.", ActiveMirror);
            return null;
        }

        var mirror = ActiveMirror;
        var authUrl = GetAuthUrl();
        string successMessage = string.Format("auth.success", "Shikimori");
        var code = await OAuthHelper.AuthorizeViaLoopbackAsync(authUrl, AppConstants.Api.RedirectUri, successMessage);

        if (string.IsNullOrEmpty(code)) return null;

        var tokens = await ExchangeCodeForTokenAsync(code, mirror);
        if (tokens != null) tokens.Mirror = mirror;
        return tokens;
    }


}
