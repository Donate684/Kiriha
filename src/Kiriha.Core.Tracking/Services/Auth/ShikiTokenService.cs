using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;

namespace Kiriha.Core.Tracking.Auth;

public class ShikiTokenService
{
    private readonly ISettingsService _settingsService;
    private readonly ShikiAuthService _authService;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public ShikiTokenService(ISettingsService settingsService, ShikiAuthService authService)
    {
        _settingsService = settingsService;
        _authService = authService;
    }

    public Task<string?> EnsureValidTokenAsync(CancellationToken ct) => EnsureValidTokenAsync(null, ct);

    public async Task<string?> EnsureValidTokenAsync(ShikiMirror? mirror, CancellationToken ct = default)
    {
        var trackerId = mirror == ShikiMirror.Net 
            ? TrackerConstants.Ids.ShikiFork 
            : (mirror == ShikiMirror.One ? TrackerConstants.Ids.ShikiOrig : null);
        var pinned = trackerId == null ? null : TrackerRequestAccount.Pinned(trackerId);

        ShikiTokens? GetTokens()
        {
            if (pinned != null)
                return pinned.Tokens as ShikiTokens;

            if (trackerId != null)
            {
                return _settingsService.Current.Api.GetAccount(trackerId)?.Tokens as ShikiTokens;
            }
            return _settingsService.Current.Api.Shiki;
        }

        var tokens = GetTokens();
        if (tokens is null) return null;
        if (!tokens.IsExpired) return tokens.AccessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            tokens = GetTokens();
            if (tokens is null || !tokens.IsExpired) return tokens?.AccessToken;

            var targetMirror = mirror ?? tokens.Mirror;
            var newTokens = await _authService.RefreshTokenAsync(tokens.RefreshToken, targetMirror, ct);
            if (newTokens != null)
            {
                newTokens.UserId = tokens.UserId;
                newTokens.Mirror = targetMirror;

                bool changed = false;
                _settingsService.Update(settings =>
                {
                    if (trackerId != null)
                    {
                        var acc = settings.Api.GetAccount(trackerId);
                        if (acc is { IsEnabled: true, Tokens: ShikiTokens current } &&
                            (pinned is null || acc.Id == pinned.Id) && SameCredentials(current, tokens))
                        {
                            acc.Tokens = newTokens;
                            changed = true;
                        }
                    }
                    else if (settings.Api.Shiki is { } current && SameCredentials(current, tokens) &&
                        settings.Api.ShikiMirror == targetMirror)
                    {
                        settings.Api.Shiki = newTokens;
                        changed = true;
                    }
                }, SettingsSection.Api, save: false);
                if (changed)
                    _settingsService.SaveImmediate();
                if (pinned != null)
                    pinned.Tokens = newTokens;
                return newTokens.AccessToken;
            }
            return null;
        }
        finally { _tokenLock.Release(); }
    }

    private static bool SameCredentials(ShikiTokens current, ShikiTokens previous) =>
        current.AccessToken == previous.AccessToken && current.RefreshToken == previous.RefreshToken;
}
