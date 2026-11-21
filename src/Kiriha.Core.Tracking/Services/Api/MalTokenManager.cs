using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Tracking.Auth;
using Serilog;

namespace Kiriha.Core.Tracking.Api;

public class MalTokenManager : IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly MalAuthService _authService;
    private readonly SemaphoreSlim _tokenRefreshLock = new(1, 1);

    public MalTokenManager(ISettingsService settingsService, MalAuthService authService)
    {
        _settingsService = settingsService;
        _authService = authService;
    }

    public async Task<string?> EnsureValidTokenAsync(CancellationToken ct = default, bool forceRefresh = false, string? failedToken = null)
    {
        var account = TrackerRequestAccount.Pinned(TrackerConstants.Ids.Mal);
        var api = _settingsService.Current.Api;
        var loginId = account?.Id ?? api.GetAccount(TrackerConstants.Ids.Mal)?.Id;
        var tokens = account is null ? api.Mal : account.Tokens as MalTokens;
        if (tokens is null) return null;
        if (!forceRefresh && !tokens.IsExpired) return tokens.AccessToken;

        await _tokenRefreshLock.WaitAsync(ct);
        try
        {
            api = _settingsService.Current.Api;
            if (api.GetAccount(TrackerConstants.Ids.Mal)?.Id == loginId && api.Mal is { } current)
            {
                tokens = current;
                if (account != null)
                    account.Tokens = current.Clone();
            }

            // If another thread already refreshed the token while we were waiting for the lock,
            // we can use the freshly obtained token if it is not expired and differs from the failed token.
            if (!tokens.IsExpired && (failedToken != null ? tokens.AccessToken != failedToken : !forceRefresh))
            {
                return tokens.AccessToken;
            }

            var newTokens = await _authService.RefreshTokenAsync(tokens.RefreshToken, ct);
            if (newTokens != null)
            {
                newTokens.UserId = tokens.UserId;
                SaveReplacement(loginId, tokens, newTokens);
                if (account != null)
                    account.Tokens = newTokens;
                return newTokens.AccessToken;
            }

            // A failed refresh can be a network outage. Keep the login and its queued edits.
            Log.Warning("MalTokenManager: refresh failed; keeping saved credentials");
            return null;
        }
        finally { _tokenRefreshLock.Release(); }
    }

    private void SaveReplacement(string? loginId, MalTokens previous, MalTokens replacement)
    {
        bool changed = false;
        _settingsService.Update(settings =>
        {
            if (settings.Api.GetAccount(TrackerConstants.Ids.Mal)?.Id != loginId ||
                settings.Api.Mal is not { } current || current.AccessToken != previous.AccessToken ||
                current.RefreshToken != previous.RefreshToken)
                return;

            settings.Api.Mal = replacement;
            changed = true;
        }, SettingsSection.Api, save: false);
        if (changed)
            _settingsService.SaveImmediate();
    }

    public void Dispose()
    {
        _tokenRefreshLock.Dispose();
    }
}
