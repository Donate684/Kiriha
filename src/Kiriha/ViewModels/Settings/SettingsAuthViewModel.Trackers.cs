using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Serilog;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsAuthViewModel
{
    private CancellationTokenSource? _authCts;

    [ObservableProperty]
    private bool _isAuthDialogOpen;

    [ObservableProperty]
    private string? _activeAuthTrackerId;

    [ObservableProperty]
    private string? _activeAuthTrackerName;

    [ObservableProperty]
    private string? _activeAuthTrackerBadge;

    [ObservableProperty]
    private string? _activeAuthTrackerDomain;

    [ObservableProperty]
    private string? _activeAuthBadgeBackground;

    [ObservableProperty]
    private string? _activeAuthStatusText;

    [ObservableProperty]
    private bool _isAuthFailed;

    [ObservableProperty]
    private string? _authErrorMessage;

    [RelayCommand]
    public void CancelAuth()
    {
        if (_authCts != null && !_authCts.IsCancellationRequested)
        {
            try
            {
                _authCts.Cancel();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Exception while cancelling auth token source");
            }
        }

        IsAuthDialogOpen = false;
        IsAuthFailed = false;
        AuthErrorMessage = null;
        ActiveAuthTrackerId = null;
    }

    public async Task<bool> ExecuteAuthFlowAsync(
        string trackerId,
        string name,
        string badge,
        string domain,
        string badgeBackground,
        Func<CancellationToken, Task<bool>> loginAction)
    {
        if (IsAuthDialogOpen) return false;

        _authCts?.Dispose();
        _authCts = new CancellationTokenSource();

        ActiveAuthTrackerId = trackerId;
        ActiveAuthTrackerName = name;
        ActiveAuthTrackerBadge = badge;
        ActiveAuthTrackerDomain = domain;
        ActiveAuthBadgeBackground = badgeBackground;
        IsAuthFailed = false;
        AuthErrorMessage = null;
        ActiveAuthStatusText = _localizer.GetLoc("settings.accounts_hub.auth_dialog_waiting");
        IsAuthDialogOpen = true;

        try
        {
            var success = await loginAction(_authCts.Token);
            if (success)
            {
                IsAuthDialogOpen = false;
                return true;
            }

            if (_authCts.IsCancellationRequested)
            {
                IsAuthDialogOpen = false;
                return false;
            }

            IsAuthFailed = true;
            AuthErrorMessage = _localizer.GetLoc("settings.accounts_hub.auth_dialog_failed");
            return false;
        }
        catch (OperationCanceledException)
        {
            IsAuthDialogOpen = false;
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error during authorization for {TrackerId}", trackerId);
            IsAuthFailed = true;
            AuthErrorMessage = _localizer.GetLoc("settings.accounts_hub.auth_dialog_failed");
            return false;
        }
        finally
        {
            if (!IsAuthFailed)
            {
                IsAuthDialogOpen = false;
            }
        }
    }

    [RelayCommand]
    public async Task MalLogin()
    {
        await ExecuteAuthFlowAsync(
            TrackerConstants.Ids.Mal,
            TrackerConstants.Names.Mal,
            "MAL",
            TrackerConstants.Domains.Mal,
            "#2e51a2",
            async ct =>
            {
                var tokens = await _authService.LoginAsync(ct);
                if (tokens != null)
                {
                    _settingsService.Update(settings =>
                    {
                        UpsertConnectedAccount(settings, TrackerConstants.Ids.Mal, tokens);
                    }, SettingsSection.Api, save: false);
                    _settingsService.SaveImmediate();
                    RefreshState();
                    return true;
                }
                return false;
            });
    }

    [RelayCommand]
    public void MalLogout()
    {
        _settingsService.Update(settings =>
        {
            var acc = settings.Api.GetAccount(TrackerConstants.Ids.Mal);
            if (acc != null)
            {
                settings.Api.Accounts.Remove(acc);
                settings.Api.EnsurePrimaryIntegrity();
            }
        }, SettingsSection.Api, save: false);
        _settingsService.SaveImmediate();
        RefreshState();
    }

    [RelayCommand]
    public async Task ShikiOrigLogin()
    {
        await ExecuteAuthFlowAsync(
            TrackerConstants.Ids.ShikiOrig,
            TrackerConstants.Domains.ShikiOrig,
            "ORIGINAL",
            TrackerConstants.Domains.ShikiOrig,
            "#2a9c5b",
            async ct =>
            {
                _shikiHostResolver.Reset();
                var tokens = await _shikiAuthService.LoginAsync(ShikiMirror.One, ct);
                if (tokens != null)
                {
                    tokens.Mirror = ShikiMirror.One;
                    _settingsService.Update(settings =>
                    {
                        var forkAcc = settings.Api.GetAccount(TrackerConstants.Ids.ShikiFork);
                        if (forkAcc != null) settings.Api.Accounts.Remove(forkAcc);

                        settings.Api.ShikiMirror = ShikiMirror.One;
                        UpsertConnectedAccount(settings, TrackerConstants.Ids.ShikiOrig, tokens);
                    }, SettingsSection.Api, save: false);
                    _settingsService.SaveImmediate();
                    _isShikiOrigSelected = true;
                    _isShikiForkSelected = false;
                    OnPropertyChanged(nameof(IsShikiOrigSelected));
                    OnPropertyChanged(nameof(IsShikiForkSelected));
                    RefreshState();
                    return true;
                }
                return false;
            });
    }

    [RelayCommand]
    public void ShikiOrigLogout()
    {
        _settingsService.Update(settings =>
        {
            var acc = settings.Api.GetAccount(TrackerConstants.Ids.ShikiOrig);
            if (acc != null)
            {
                settings.Api.Accounts.Remove(acc);
                settings.Api.EnsurePrimaryIntegrity();
            }
        }, SettingsSection.Api, save: false);
        _settingsService.SaveImmediate();
        RefreshState();
    }

    [RelayCommand]
    public Task ShikiLoginOne() => ShikiOrigLogin();

    [RelayCommand]
    public async Task ShikiForkLogin()
    {
        await ExecuteAuthFlowAsync(
            TrackerConstants.Ids.ShikiFork,
            TrackerConstants.Domains.ShikiFork,
            "FORK",
            TrackerConstants.Domains.ShikiFork,
            "#cc8400",
            async ct =>
            {
                _shikiHostResolver.Reset();
                var tokens = await _shikiAuthService.LoginAsync(ShikiMirror.Net, ct);
                if (tokens != null)
                {
                    tokens.Mirror = ShikiMirror.Net;
                    _settingsService.Update(settings =>
                    {
                        var origAcc = settings.Api.GetAccount(TrackerConstants.Ids.ShikiOrig);
                        if (origAcc != null) settings.Api.Accounts.Remove(origAcc);

                        settings.Api.ShikiMirror = ShikiMirror.Net;
                        UpsertConnectedAccount(settings, TrackerConstants.Ids.ShikiFork, tokens);
                    }, SettingsSection.Api, save: false);
                    _settingsService.SaveImmediate();
                    _isShikiForkSelected = true;
                    _isShikiOrigSelected = false;
                    OnPropertyChanged(nameof(IsShikiOrigSelected));
                    OnPropertyChanged(nameof(IsShikiForkSelected));
                    RefreshState();
                    return true;
                }
                return false;
            });
    }

    [RelayCommand]
    public void ShikiForkLogout()
    {
        _settingsService.Update(settings =>
        {
            var acc = settings.Api.GetAccount(TrackerConstants.Ids.ShikiFork);
            if (acc != null)
            {
                settings.Api.Accounts.Remove(acc);
                settings.Api.EnsurePrimaryIntegrity();
            }
        }, SettingsSection.Api, save: false);
        _settingsService.SaveImmediate();
        RefreshState();
    }

    [RelayCommand]
    public Task ShikiLoginNet() => ShikiForkLogin();

    [RelayCommand]
    public void ShikiLogout()
    {
        _settingsService.Update(settings =>
        {
            var orig = settings.Api.GetAccount(TrackerConstants.Ids.ShikiOrig);
            if (orig != null) settings.Api.Accounts.Remove(orig);

            var fork = settings.Api.GetAccount(TrackerConstants.Ids.ShikiFork);
            if (fork != null) settings.Api.Accounts.Remove(fork);

            settings.Api.EnsurePrimaryIntegrity();
        }, SettingsSection.Api, save: false);
        _settingsService.SaveImmediate();
        RefreshState();
    }

    [RelayCommand]
    public async Task AniListLogin()
    {
        await ExecuteAuthFlowAsync(
            TrackerConstants.Ids.AniList,
            TrackerConstants.Names.AniList,
            "AL",
            TrackerConstants.Domains.AniList,
            "#02a9ff",
            async ct =>
            {
                var tokens = await _aniListAuthService.LoginAsync(ct);
                if (tokens != null)
                {
                    _settingsService.Update(settings =>
                    {
                        UpsertConnectedAccount(settings, TrackerConstants.Ids.AniList, tokens, tokens.UserName);
                    }, SettingsSection.Api, save: false);
                    _settingsService.SaveImmediate();
                    RefreshState();
                    return true;
                }
                return false;
            });
    }

    [RelayCommand]
    public void AniListLogout()
    {
        _settingsService.Update(settings =>
        {
            var acc = settings.Api.GetAccount(TrackerConstants.Ids.AniList);
            if (acc != null)
            {
                settings.Api.Accounts.Remove(acc);
                settings.Api.EnsurePrimaryIntegrity();
            }
        }, SettingsSection.Api, save: false);
        _settingsService.SaveImmediate();
        RefreshState();
    }
}
