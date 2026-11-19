using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Constants;
using Serilog;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsAuthViewModel
{
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
        ? (SyncStatusText.StartsWith("sync.", StringComparison.Ordinal) ? _localizer.GetLoc(SyncStatusText) : SyncStatusText)
        : _localizer.GetLoc("settings.accounts_hub.switch_sync_desc");

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
        if (_refreshService == null)
        {
            await Task.Delay(400, CancellationToken.None);
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

            var success = await _refreshService.RefreshAnimeListAsync(progress, cts.Token, isMigration: true);
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

        CancelAuth();
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

    public IReadOnlyList<PrimaryTrackerOption> AvailablePrimaryTrackers { get; } =
    [
        new(TrackerConstants.Ids.Mal, TrackerConstants.Names.Mal, "MAL", TrackerConstants.Domains.Mal),
        new(TrackerConstants.Ids.AniList, TrackerConstants.Names.AniList, "AL", TrackerConstants.Domains.AniList),
        new(TrackerConstants.Ids.ShikiOrig, TrackerConstants.Domains.ShikiOrig, "ORIGINAL", TrackerConstants.Domains.ShikiOrig),
        new(TrackerConstants.Ids.ShikiFork, TrackerConstants.Domains.ShikiFork, "FORK", TrackerConstants.Domains.ShikiFork)
    ];

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

    public void UpdatePrimaryTrackerOptionsAvailability()
    {
        var disabledHint = _localizer.GetLoc("settings.accounts_hub.mirror_inactive_hint");

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
