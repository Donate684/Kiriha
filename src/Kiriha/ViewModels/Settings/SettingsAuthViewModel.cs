using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Auth;
using Serilog;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsAuthViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly MalAuthService _authService;
    private readonly ShikiAuthService _shikiAuthService;
    private readonly ShikiHostResolver _shikiHostResolver;
    private readonly AniListAuthService _aniListAuthService;
    private readonly IAnimeSyncOrchestrator? _syncOrchestrator;

    public SettingsAuthViewModel(
        ISettingsService settingsService,
        MalAuthService authService,
        ShikiAuthService shikiAuthService,
        ShikiHostResolver shikiHostResolver,
        AniListAuthService aniListAuthService,
        IAnimeSyncOrchestrator? syncOrchestrator = null)
    {
        _settingsService = settingsService;
        _authService = authService;
        _shikiAuthService = shikiAuthService;
        _shikiHostResolver = shikiHostResolver;
        _aniListAuthService = aniListAuthService;
        _syncOrchestrator = syncOrchestrator;

        var isFork = _settingsService.Current.Api.ShikiMirror == ShikiMirror.Net
                     || _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.ShikiFork) != null;
        _isShikiForkSelected = isFork;
        _isShikiOrigSelected = !isFork;
        UpdatePrimaryTrackerOptionsAvailability();
    }

    #region Primary Switch Dialog

    [ObservableProperty]
    private bool _isSwitchDialogOpen;

    [ObservableProperty]
    private PrimarySwitchStep _switchStep = PrimarySwitchStep.None;

    [ObservableProperty]
    private PrimaryTrackerOption? _pendingPrimaryTracker;

    [ObservableProperty]
    private string? _syncStatusText;

    [ObservableProperty]
    private bool _isLoggingInForSwitch;

    public bool IsSwitchConfirm => SwitchStep == PrimarySwitchStep.Confirm;
    public bool IsSwitchRequiresAuth => SwitchStep == PrimarySwitchStep.RequiresAuth;
    public bool IsSwitchSyncing => SwitchStep == PrimarySwitchStep.Syncing;
    public bool IsSwitchSuccess => SwitchStep == PrimarySwitchStep.Success;
    public bool IsSwitchFailed => SwitchStep == PrimarySwitchStep.Failed;

    public string SyncStatusDisplay => !string.IsNullOrWhiteSpace(SyncStatusText)
        ? (SyncStatusText.StartsWith("sync.") ? Core.UIUtils.GetLoc(SyncStatusText) : SyncStatusText)
        : Core.UIUtils.GetLoc("settings.accounts_hub.switch_sync_desc");

    partial void OnSwitchStepChanged(PrimarySwitchStep value)
    {
        OnPropertyChanged(nameof(IsSwitchConfirm));
        OnPropertyChanged(nameof(IsSwitchRequiresAuth));
        OnPropertyChanged(nameof(IsSwitchSyncing));
        OnPropertyChanged(nameof(IsSwitchSuccess));
        OnPropertyChanged(nameof(IsSwitchFailed));
    }

    partial void OnSyncStatusTextChanged(string? value)
    {
        OnPropertyChanged(nameof(SyncStatusDisplay));
    }

    public void InitiatePrimarySwitch(PrimaryTrackerOption target)
    {
        if (!target.IsEnabled) return;

        PendingPrimaryTracker = target;
        IsSwitchDialogOpen = true;
        SwitchStep = PrimarySwitchStep.Confirm;
        SyncStatusText = null;
    }

    [RelayCommand]
    public void ConfirmPrimarySwitch()
    {
        if (PendingPrimaryTracker == null) return;

        if (!IsTrackerConnected(PendingPrimaryTracker.Id))
        {
            SwitchStep = PrimarySwitchStep.RequiresAuth;
            SyncStatusText = null;
        }
        else
        {
            ExecutePrimarySwitchAndSync(PendingPrimaryTracker.Id);
        }
    }

    public void ExecutePrimarySwitchAndSync(string trackerId)
    {
        SetPrimary(trackerId);
        SwitchStep = PrimarySwitchStep.Syncing;
        SyncStatusText = null;
        _ = RunDatabaseSyncAsync();
    }

    public async Task RunDatabaseSyncAsync()
    {
        if (_syncOrchestrator == null)
        {
            await Task.Delay(400);
            SwitchStep = PrimarySwitchStep.Success;
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var progress = new Progress<string>(status =>
            {
                SyncStatusText = status;
            });

            var success = await _syncOrchestrator.SyncWithTrackersAsync(progress, cts.Token, isMigration: true);
            SwitchStep = success ? PrimarySwitchStep.Success : PrimarySwitchStep.Failed;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to sync database during primary tracker switch");
            SwitchStep = PrimarySwitchStep.Failed;
        }
    }

    [RelayCommand]
    public async Task ExecutePendingLogin()
    {
        if (PendingPrimaryTracker == null || IsLoggingInForSwitch) return;

        IsLoggingInForSwitch = true;
        try
        {
            switch (PendingPrimaryTracker.Id)
            {
                case TrackerConstants.Ids.Mal:
                    await MalLogin();
                    break;
                case TrackerConstants.Ids.AniList:
                    await AniListLogin();
                    break;
                case TrackerConstants.Ids.ShikiOrig:
                    await ShikiOrigLogin();
                    break;
                case TrackerConstants.Ids.ShikiFork:
                    await ShikiForkLogin();
                    break;
            }

            if (IsTrackerConnected(PendingPrimaryTracker.Id))
            {
                ExecutePrimarySwitchAndSync(PendingPrimaryTracker.Id);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed during login for primary tracker switch");
        }
        finally
        {
            IsLoggingInForSwitch = false;
        }
    }

    [RelayCommand]
    public void CancelPrimarySwitch()
    {
        if (IsSwitchSyncing) return;

        IsSwitchDialogOpen = false;
        SwitchStep = PrimarySwitchStep.None;
        PendingPrimaryTracker = null;
        SyncStatusText = null;
        OnPropertyChanged(nameof(SelectedPrimaryTracker));
    }

    [RelayCommand]
    public void ClosePrimarySwitchDialog()
    {
        IsSwitchDialogOpen = false;
        SwitchStep = PrimarySwitchStep.None;
        PendingPrimaryTracker = null;
        SyncStatusText = null;
        OnPropertyChanged(nameof(SelectedPrimaryTracker));
    }

    public bool IsTrackerConnected(string trackerId) => trackerId switch
    {
        TrackerConstants.Ids.Mal => IsMalConnected,
        TrackerConstants.Ids.AniList => IsAniListConnected,
        TrackerConstants.Ids.ShikiOrig => IsShikiOrigConnected,
        TrackerConstants.Ids.ShikiFork => IsShikiForkConnected,
        _ => false
    };

    #endregion

    #region Sync Hub

    public IReadOnlyList<PrimaryTrackerOption> AvailablePrimaryTrackers { get; } = new List<PrimaryTrackerOption>
    {
        new(TrackerConstants.Ids.Mal, TrackerConstants.Names.Mal, "MAL", TrackerConstants.Domains.Mal),
        new(TrackerConstants.Ids.AniList, TrackerConstants.Names.AniList, "AL", TrackerConstants.Domains.AniList),
        new(TrackerConstants.Ids.ShikiOrig, TrackerConstants.Domains.ShikiOrig, "ORIGINAL", TrackerConstants.Domains.ShikiOrig),
        new(TrackerConstants.Ids.ShikiFork, TrackerConstants.Domains.ShikiFork, "FORK", TrackerConstants.Domains.ShikiFork)
    };

    public PrimaryTrackerOption? SelectedPrimaryTracker
    {
        get => AvailablePrimaryTrackers.FirstOrDefault(t => string.Equals(t.Id, _settingsService.Current.Api.PrimaryTrackerId, StringComparison.OrdinalIgnoreCase))
               ?? AvailablePrimaryTrackers[0];
        set
        {
            if (value == null || !value.IsEnabled)
            {
                OnPropertyChanged();
                return;
            }

            if (!string.Equals(_settingsService.Current.Api.PrimaryTrackerId, value.Id, StringComparison.OrdinalIgnoreCase))
            {
                InitiatePrimarySwitch(value);
            }
        }
    }

    public string PrimaryTrackerName => TrackerConstants.GetDefaultDisplayName(_settingsService.Current.Api.PrimaryTrackerId ?? TrackerConstants.Ids.Mal);

    public string PrimaryTrackerBadgeText => _settingsService.Current.Api.PrimaryTrackerId switch
    {
        TrackerConstants.Ids.Mal => "MAL",
        TrackerConstants.Ids.AniList => "AL",
        _ => "SH"
    };

    public bool IsPrimaryConnected => _settingsService.Current.Api.PrimaryTrackerId switch
    {
        TrackerConstants.Ids.Mal => IsMalConnected,
        TrackerConstants.Ids.AniList => IsAniListConnected,
        TrackerConstants.Ids.ShikiOrig => IsShikiOrigConnected,
        TrackerConstants.Ids.ShikiFork => IsShikiForkConnected,
        _ => false
    };

    public bool IsMalActiveMirror => IsMalConnected && !IsMalPrimary;
    public bool IsAniListActiveMirror => IsAniListConnected && !IsAniListPrimary;
    public bool IsShikiOrigActiveMirror => IsShikiOrigConnected && !IsShikiOrigPrimary;
    public bool IsShikiForkActiveMirror => IsShikiForkConnected && !IsShikiForkPrimary;

    public bool HasActiveMirrors => IsMalActiveMirror || IsAniListActiveMirror || IsShikiActiveMirror;
    public bool HasNoActiveMirrors => !HasActiveMirrors;

    #endregion

    #region Connection Status

    public bool IsMalConnected => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.Mal) != null;
    public bool IsShikiOrigConnected => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.ShikiOrig) != null;
    public bool IsShikiForkConnected => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.ShikiFork) != null;
    public bool IsAniListConnected => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.AniList) != null;

    #endregion

    #region Primary Tracker State

    public bool IsMalPrimary => string.Equals(_settingsService.Current.Api.PrimaryTrackerId, TrackerConstants.Ids.Mal, StringComparison.OrdinalIgnoreCase);
    public bool IsShikiOrigPrimary => string.Equals(_settingsService.Current.Api.PrimaryTrackerId, TrackerConstants.Ids.ShikiOrig, StringComparison.OrdinalIgnoreCase);
    public bool IsShikiForkPrimary => string.Equals(_settingsService.Current.Api.PrimaryTrackerId, TrackerConstants.Ids.ShikiFork, StringComparison.OrdinalIgnoreCase);
    public bool IsAniListPrimary => string.Equals(_settingsService.Current.Api.PrimaryTrackerId, TrackerConstants.Ids.AniList, StringComparison.OrdinalIgnoreCase);

    #endregion

    #region Shikimori Active Target (Either Fork OR Original)

    public bool IsShikiAnyConnected => IsShikiOrigConnected || IsShikiForkConnected;
    public bool IsShikiCanSwitch => !IsShikiAnyConnected;

    private bool _isShikiForkSelected;
    public bool IsShikiForkSelected
    {
        get => _isShikiForkSelected;
        set
        {
            if (value && !_isShikiForkSelected)
            {
                if (IsShikiAnyConnected) return;
                _isShikiForkSelected = true;
                _isShikiOrigSelected = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsShikiOrigSelected));
                _settingsService.Update(s => s.Api.ShikiMirror = ShikiMirror.Net, SettingsSection.Api, save: false);
                _settingsService.SaveImmediate();
                NotifyShikiActiveProperties();
            }
        }
    }

    private bool _isShikiOrigSelected = true;
    public bool IsShikiOrigSelected
    {
        get => _isShikiOrigSelected;
        set
        {
            if (value && !_isShikiOrigSelected)
            {
                if (IsShikiAnyConnected) return;
                _isShikiOrigSelected = true;
                _isShikiForkSelected = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsShikiForkSelected));
                _settingsService.Update(s => s.Api.ShikiMirror = ShikiMirror.One, SettingsSection.Api, save: false);
                _settingsService.SaveImmediate();
                NotifyShikiActiveProperties();
            }
        }
    }

    public bool IsShikiActiveConnected => IsShikiForkSelected ? IsShikiForkConnected : IsShikiOrigConnected;
    public bool IsShikiActivePrimary => IsShikiForkSelected ? IsShikiForkPrimary : IsShikiOrigPrimary;
    public bool IsShikiActiveMirror => IsShikiActiveConnected && !IsShikiActivePrimary;
    public string ShikiActiveDomain => IsShikiForkSelected ? TrackerConstants.Domains.ShikiFork : TrackerConstants.Domains.ShikiOrig;
    public string ShikiActiveDescription => IsShikiForkSelected
        ? Core.UIUtils.GetLoc("settings.accounts_hub.shikimori_fork_sub")
        : Core.UIUtils.GetLoc("settings.accounts_hub.shikimori_orig_sub");
    public string? ShikiActiveUsername => IsShikiForkSelected ? ShikiForkUsername : ShikiOrigUsername;
    public IRelayCommand ShikiActiveLoginCommand => IsShikiForkSelected ? ShikiForkLoginCommand : ShikiOrigLoginCommand;
    public IRelayCommand ShikiActiveLogoutCommand => IsShikiForkSelected ? ShikiForkLogoutCommand : ShikiOrigLogoutCommand;

    public void NotifyShikiActiveProperties()
    {
        UpdatePrimaryTrackerOptionsAvailability();

        OnPropertyChanged(nameof(IsShikiAnyConnected));
        OnPropertyChanged(nameof(IsShikiCanSwitch));
        OnPropertyChanged(nameof(IsShikiOrigSelected));
        OnPropertyChanged(nameof(IsShikiForkSelected));
        OnPropertyChanged(nameof(IsShikiActiveConnected));
        OnPropertyChanged(nameof(IsShikiActivePrimary));
        OnPropertyChanged(nameof(IsShikiActiveMirror));
        OnPropertyChanged(nameof(ShikiActiveDomain));
        OnPropertyChanged(nameof(ShikiActiveDescription));
        OnPropertyChanged(nameof(ShikiActiveUsername));
        OnPropertyChanged(nameof(ShikiActiveLoginCommand));
        OnPropertyChanged(nameof(ShikiActiveLogoutCommand));
        OnPropertyChanged(nameof(HasActiveMirrors));
        OnPropertyChanged(nameof(HasNoActiveMirrors));
        OnPropertyChanged(nameof(IsPrimaryConnected));
    }

    #endregion

    #region Mirror Tracker State

    public bool IsMalMirror
    {
        get => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.Mal)?.IsMirror ?? false;
        set
        {
            _settingsService.Update(s =>
            {
                var acc = s.Api.GetAccount(TrackerConstants.Ids.Mal);
                if (acc != null) acc.IsMirror = value;
            }, SettingsSection.Api, save: false);
            _settingsService.SaveImmediate();
            RefreshState();
        }
    }

    public bool IsShikiOrigMirror
    {
        get => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.ShikiOrig)?.IsMirror ?? false;
        set
        {
            _settingsService.Update(s =>
            {
                var acc = s.Api.GetAccount(TrackerConstants.Ids.ShikiOrig);
                if (acc != null) acc.IsMirror = value;
            }, SettingsSection.Api, save: false);
            _settingsService.SaveImmediate();
            RefreshState();
        }
    }

    public bool IsShikiForkMirror
    {
        get => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.ShikiFork)?.IsMirror ?? false;
        set
        {
            _settingsService.Update(s =>
            {
                var acc = s.Api.GetAccount(TrackerConstants.Ids.ShikiFork);
                if (acc != null) acc.IsMirror = value;
            }, SettingsSection.Api, save: false);
            _settingsService.SaveImmediate();
            RefreshState();
        }
    }

    public bool IsAniListMirror
    {
        get => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.AniList)?.IsMirror ?? false;
        set
        {
            _settingsService.Update(s =>
            {
                var acc = s.Api.GetAccount(TrackerConstants.Ids.AniList);
                if (acc != null) acc.IsMirror = value;
            }, SettingsSection.Api, save: false);
            _settingsService.SaveImmediate();
            RefreshState();
        }
    }

    #endregion

    #region Usernames

    public string? MalUsername => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.Mal)?.Username;
    public string? ShikiOrigUsername => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.ShikiOrig)?.Username;
    public string? ShikiForkUsername => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.ShikiFork)?.Username;
    public string? AniListUsername => _settingsService.Current.Api.GetAccount(TrackerConstants.Ids.AniList)?.Username;

    #endregion

    #region Backward Compatibility Properties

    public bool IsLoggedIn => IsMalConnected;
    public bool IsShikiLoggedIn => IsShikiOrigConnected || IsShikiForkConnected;
    public bool IsShikiOneConnected => IsShikiOrigConnected;
    public bool IsShikiNetConnected => IsShikiForkConnected;
    public bool CanLoginShikiOne => true;
    public bool CanLoginShikiNet => true;

    #endregion

    #region Primary Selection

    [RelayCommand]
    public void SetPrimary(string trackerId)
    {
        _settingsService.Update(settings =>
        {
            settings.Api.SetPrimaryAccount(trackerId);
        }, SettingsSection.Api, save: false);
        _settingsService.SaveImmediate();
        RefreshState();
    }

    [RelayCommand]
    public void SetMalPrimary() => SetPrimary(TrackerConstants.Ids.Mal);

    [RelayCommand]
    public void SetShikiOrigPrimary()
    {
        if (IsShikiForkSelected) return;
        SetPrimary(TrackerConstants.Ids.ShikiOrig);
    }

    [RelayCommand]
    public void SetShikiForkPrimary()
    {
        if (IsShikiOrigSelected) return;
        SetPrimary(TrackerConstants.Ids.ShikiFork);
    }

    [RelayCommand]
    public void SetAniListPrimary() => SetPrimary(TrackerConstants.Ids.AniList);

    #endregion

    #region MAL Commands

    [RelayCommand]
    public async Task MalLogin()
    {
        var tokens = await _authService.LoginAsync();
        if (tokens != null)
        {
            _settingsService.Update(settings =>
            {
                UpsertConnectedAccount(settings, TrackerConstants.Ids.Mal, tokens);
            }, SettingsSection.Api, save: false);
            _settingsService.SaveImmediate();
            RefreshState();
        }
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

    #endregion

    #region Shikimori Original Commands

    [RelayCommand]
    public async Task ShikiOrigLogin()
    {
        _shikiHostResolver.Reset();
        var tokens = await _shikiAuthService.LoginAsync(ShikiMirror.One);
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
        }
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

    #endregion

    #region Shikimori Fork Commands

    [RelayCommand]
    public async Task ShikiForkLogin()
    {
        _shikiHostResolver.Reset();
        var tokens = await _shikiAuthService.LoginAsync(ShikiMirror.Net);
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
        }
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

    #endregion

    #region AniList Commands

    [RelayCommand]
    public async Task AniListLogin()
    {
        var tokens = await _aniListAuthService.LoginAsync();
        if (tokens != null)
        {
            _settingsService.Update(settings =>
            {
                UpsertConnectedAccount(settings, TrackerConstants.Ids.AniList, tokens, tokens.UserName);
            }, SettingsSection.Api, save: false);
            _settingsService.SaveImmediate();
            RefreshState();
        }
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

    #endregion

    public void RefreshState()
    {
        OnPropertyChanged(nameof(IsMalConnected));
        OnPropertyChanged(nameof(IsShikiOrigConnected));
        OnPropertyChanged(nameof(IsShikiForkConnected));
        OnPropertyChanged(nameof(IsAniListConnected));

        OnPropertyChanged(nameof(IsMalPrimary));
        OnPropertyChanged(nameof(IsShikiOrigPrimary));
        OnPropertyChanged(nameof(IsShikiForkPrimary));
        OnPropertyChanged(nameof(IsAniListPrimary));

        OnPropertyChanged(nameof(IsMalMirror));
        OnPropertyChanged(nameof(IsShikiOrigMirror));
        OnPropertyChanged(nameof(IsShikiForkMirror));
        OnPropertyChanged(nameof(IsAniListMirror));

        OnPropertyChanged(nameof(MalUsername));
        OnPropertyChanged(nameof(ShikiOrigUsername));
        OnPropertyChanged(nameof(ShikiForkUsername));
        OnPropertyChanged(nameof(AniListUsername));

        OnPropertyChanged(nameof(PrimaryTrackerName));
        OnPropertyChanged(nameof(PrimaryTrackerBadgeText));
        OnPropertyChanged(nameof(SelectedPrimaryTracker));
        OnPropertyChanged(nameof(IsPrimaryConnected));

        OnPropertyChanged(nameof(IsMalActiveMirror));
        OnPropertyChanged(nameof(IsAniListActiveMirror));
        OnPropertyChanged(nameof(IsShikiOrigActiveMirror));
        OnPropertyChanged(nameof(IsShikiForkActiveMirror));
        OnPropertyChanged(nameof(HasActiveMirrors));
        OnPropertyChanged(nameof(HasNoActiveMirrors));

        // Backward compatibility
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(IsShikiLoggedIn));
        OnPropertyChanged(nameof(IsShikiOneConnected));
        OnPropertyChanged(nameof(IsShikiNetConnected));
        OnPropertyChanged(nameof(CanLoginShikiOne));
        OnPropertyChanged(nameof(CanLoginShikiNet));

        if (IsShikiForkConnected)
        {
            _isShikiForkSelected = true;
            _isShikiOrigSelected = false;
        }
        else if (IsShikiOrigConnected)
        {
            _isShikiOrigSelected = true;
            _isShikiForkSelected = false;
        }

        NotifyShikiActiveProperties();
    }

    private static void UpsertConnectedAccount(AppSettings settings, string trackerId, OAuthTokens tokens, string? username = null)
    {
        var acc = settings.Api.GetAccount(trackerId);
        var isPrimaryTarget = string.Equals(trackerId, settings.Api.PrimaryTrackerId, StringComparison.OrdinalIgnoreCase);
        var shouldBePrimary = isPrimaryTarget || settings.Api.Accounts.All(a => !a.IsPrimary);

        if (acc == null)
        {
            acc = new TrackerAccount
            {
                TrackerId = trackerId,
                IsEnabled = true,
                IsPrimary = shouldBePrimary,
                IsMirror = !shouldBePrimary
            };
            settings.Api.Accounts.Add(acc);
        }
        else
        {
            acc.IsEnabled = true;
            if (shouldBePrimary)
            {
                acc.IsPrimary = true;
                acc.IsMirror = false;
            }
        }

        acc.Tokens = tokens;
        if (!string.IsNullOrEmpty(username)) acc.Username = username;
        if (shouldBePrimary)
        {
            settings.Api.PrimaryTrackerId = trackerId;
        }
        settings.Api.EnsurePrimaryIntegrity();
    }

    public void UpdatePrimaryTrackerOptionsAvailability()
    {
        var disabledHint = Core.UIUtils.GetLoc("settings.accounts_hub.mirror_inactive_hint");

        foreach (var option in AvailablePrimaryTrackers)
        {
            if (string.Equals(option.Id, TrackerConstants.Ids.ShikiOrig, StringComparison.OrdinalIgnoreCase))
            {
                option.IsEnabled = !IsShikiForkSelected;
                option.DisabledReason = !IsShikiForkSelected ? null : disabledHint;
            }
            else if (string.Equals(option.Id, TrackerConstants.Ids.ShikiFork, StringComparison.OrdinalIgnoreCase))
            {
                option.IsEnabled = IsShikiForkSelected;
                option.DisabledReason = IsShikiForkSelected ? null : disabledHint;
            }
            else
            {
                option.IsEnabled = true;
                option.DisabledReason = null;
            }
        }
    }
}

public partial class PrimaryTrackerOption : ObservableObject
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Badge { get; init; }
    public string Domain { get; init; }

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string? _disabledReason;

    public PrimaryTrackerOption(string id, string name, string badge, string domain, bool isEnabled = true, string? disabledReason = null)
    {
        Id = id;
        Name = name;
        Badge = badge;
        Domain = domain;
        _isEnabled = isEnabled;
        _disabledReason = disabledReason;
    }
}

public enum PrimarySwitchStep
{
    None = 0,
    Confirm,
    RequiresAuth,
    Syncing,
    Success,
    Failed
}
