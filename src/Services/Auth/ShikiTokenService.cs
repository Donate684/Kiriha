using System;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Services.Data.Settings;

namespace Kiriha.Services.Auth;

public class ShikiTokenService
{
    private readonly SettingsService _settingsService;
    private readonly ShikiAuthService _authService;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public ShikiTokenService(SettingsService settingsService, ShikiAuthService authService)
    {
        _settingsService = settingsService;
        _authService = authService;
    }

    public async Task<string?> EnsureValidTokenAsync(CancellationToken ct)
    {
        var tokens = _settingsService.Current.Api.Shiki;
        if (tokens == null) return null;
        if (!tokens.IsExpired) return tokens.AccessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            tokens = _settingsService.Current.Api.Shiki;
            if (tokens == null || !tokens.IsExpired) return tokens?.AccessToken;

            var newTokens = await _authService.RefreshTokenAsync(tokens.RefreshToken, ct);
            if (newTokens != null)
            {
                newTokens.UserId = tokens.UserId;
                _settingsService.Update(settings => settings.Api.Shiki = newTokens, SettingsSection.Api, save: false);
                _settingsService.SaveImmediate();
                return newTokens.AccessToken;
            }
            return null;
        }
        finally { _tokenLock.Release(); }
    }
}
