using Kiriha.Core.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Tracking.Auth;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

public class MalTokenManager : IDisposable
{
    private readonly Kiriha.Core.Services.ISettingsService _settingsService;
    private readonly MalAuthService _authService;
    private readonly SemaphoreSlim _tokenRefreshLock = new(1, 1);
    private int _refreshFailures;
    private const int MaxRefreshFailures = 3;

    public MalTokenManager(Kiriha.Core.Services.ISettingsService settingsService, MalAuthService authService)
    {
        _settingsService = settingsService;
        _authService = authService;
    }

    public async Task<string?> EnsureValidTokenAsync(CancellationToken ct = default, bool forceRefresh = false)
    {
        var tokens = _settingsService.Current.Api.Mal;
        if (tokens == null) return null;
        if (!forceRefresh && !tokens.IsExpired) return tokens.AccessToken;

        await _tokenRefreshLock.WaitAsync(ct);
        try
        {
            tokens = _settingsService.Current.Api.Mal;
            if (tokens == null) return null;
            if (!forceRefresh && !tokens.IsExpired) return tokens.AccessToken;

            var newTokens = await _authService.RefreshTokenAsync(tokens.RefreshToken, ct);
            if (newTokens != null)
            {
                _refreshFailures = 0;
                _settingsService.Update(settings => settings.Api.Mal = newTokens, save: false);
                _settingsService.SaveImmediate();
                return newTokens.AccessToken;
            }

            _refreshFailures++;
            if (_refreshFailures >= MaxRefreshFailures)
            {
                Log.Warning("MalTokenManager: clearing saved tokens after {Count} consecutive refresh failures. User must re-authenticate.", _refreshFailures);
                _settingsService.Update(settings => settings.Api.Mal = null, save: false);
                _settingsService.SaveImmediate();
                _refreshFailures = 0;
            }
            return null;
        }
        finally { _tokenRefreshLock.Release(); }
    }

    public void Dispose()
    {
        _tokenRefreshLock.Dispose();
    }
}
