using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;

namespace Kiriha.ViewModels.History;

/// <summary>
/// Display entry for history. Represents either a single HistoryItem or a
/// merged range of consecutive episode-watches for the same anime on the same day.
/// </summary>
public sealed class HistoryEntryVm : HistoryTimelineItem
{
    public const string NotLoggedInLocKey = "history.tracker.not_logged_in";

    public bool IsFirstInGroup { get; set; }
    public bool IsLastInGroup { get; set; }
    private readonly ILocalizer _localizer;

    public HistoryEntryVm(ILocalizer localizer)
    {
        _localizer = localizer;
    }

    public int AnimeId { get; set; }
    public string AnimeTitle { get; set; } = string.Empty;
    public string? RussianTitle { get; set; }
    public string? PosterUrl { get; set; }
    public int ActionType { get; set; }
    public string Detail { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }            // Most recent action in the group (for time display)
    public int EpisodeFrom { get; set; }
    public int EpisodeTo { get; set; }
    public int Count { get; set; } = 1;                // How many raw entries were merged
    public HistoryItem? Primary { get; set; }          // Representative raw item (for OpenDetails command)

    public bool IsRange => EpisodeFrom != EpisodeTo;
    public string EpisodeLabel =>
        (ActionType == 1 || ActionType == 4 || ActionType == 6)
            ? (IsRange
                ? _localizer.GetLoc("history.episode_range", EpisodeFrom, EpisodeTo)
                : (EpisodeFrom > 0 ? _localizer.GetLoc("history.episode_single", EpisodeFrom) : string.Empty))
            : string.Empty;

    public bool CanTrack => AnimeId > 0 && (ActionType == 1 || ActionType == 4 || ActionType == 5 || ActionType == 6 || ActionType == 7);

    public ObservableCollection<TrackerStatusBadgeVm> TrackerBadges { get; } = new();
    public bool HasTrackerBadges => CanTrack && TrackerBadges.Count > 0;

    private Func<string, Task>? _onSync;
    private Func<string, bool>? _isTrackerEnabled;

    public void LoadTrackerStatuses(
        Dictionary<string, TrackerSyncInfo> statuses,
        Func<string, Task>? onSync = null,
        Func<string, bool>? isTrackerEnabled = null)
    {
        _onSync = onSync;
        _isTrackerEnabled = isTrackerEnabled;

        TrackerBadges.Clear();
        if (!CanTrack)
        {
            OnPropertyChanged(nameof(HasTrackerBadges));
            return;
        }

        var trackerNames = new List<string> { "Shikimori", "MyAnimeList" };
        foreach (var key in statuses.Keys)
        {
            if (!trackerNames.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                trackerNames.Add(key);
            }
        }

        foreach (var trackerName in trackerNames)
        {
            var shortName = trackerName switch
            {
                "Shikimori" => "Shiki",
                "MyAnimeList" => "MAL",
                _ => trackerName
            };

            var state = TrackerSyncState.NotSynced;
            string? error = null;
            if (statuses.TryGetValue(trackerName, out var info))
            {
                state = info.State;
                error = info.ErrorMessage;
            }

            bool isEnabled = _isTrackerEnabled == null || _isTrackerEnabled(trackerName);
            string tooltip = FormatTooltip(trackerName, state, error, isEnabled);

            TrackerBadges.Add(new TrackerStatusBadgeVm(
                trackerName,
                shortName,
                state,
                tooltip,
                isEnabled,
                _onSync != null ? () => _onSync(trackerName) : null));
        }
        OnPropertyChanged(nameof(HasTrackerBadges));
    }

    public void UpdateTrackerStatus(string trackerName, TrackerSyncState state, string? error, bool isEnabled = true)
    {
        Primary?.SetTrackerStatus(trackerName, state, error);
        string tooltip = FormatTooltip(trackerName, state, error, isEnabled);
        var existing = TrackerBadges.FirstOrDefault(b => string.Equals(b.TrackerName, trackerName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Update(state, tooltip, isEnabled);
        }
        else if (CanTrack)
        {
            var shortName = trackerName switch
            {
                "Shikimori" => "Shiki",
                "MyAnimeList" => "MAL",
                _ => trackerName
            };
            TrackerBadges.Add(new TrackerStatusBadgeVm(
                trackerName,
                shortName,
                state,
                tooltip,
                isEnabled,
                _onSync != null ? () => _onSync(trackerName) : null));
            OnPropertyChanged(nameof(HasTrackerBadges));
        }
    }

    private string FormatTooltip(string trackerName, TrackerSyncState state, string? error, bool isEnabled)
    {
        if (!isEnabled && state == TrackerSyncState.NotSynced)
        {
            return _localizer.GetLoc(NotLoggedInLocKey, trackerName);
        }

        return state switch
        {
            TrackerSyncState.Success => _localizer.GetLoc("history.tracker.success", trackerName),
            TrackerSyncState.Pending => _localizer.GetLoc("history.tracker.pending", trackerName),
            TrackerSyncState.Retrying => !string.IsNullOrEmpty(error)
                ? $"{trackerName}: {error}"
                : _localizer.GetLoc("history.tracker.retrying", trackerName),
            TrackerSyncState.Failed => !string.IsNullOrEmpty(error)
                ? _localizer.GetLoc("history.tracker.failed", trackerName, error)
                : _localizer.GetLoc("history.tracker.failed_generic", trackerName),
            TrackerSyncState.NotSynced => _localizer.GetLoc("history.tracker.not_synced", trackerName),
            _ => trackerName
        };
    }
}

public sealed partial class TrackerStatusBadgeVm : ObservableObject
{
    private readonly Func<Task>? _syncAction;

    public string TrackerName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    [NotifyPropertyChangedFor(nameof(IconForeground))]
    [NotifyPropertyChangedFor(nameof(BackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(BorderBrush))]
    [NotifyPropertyChangedFor(nameof(IsClickable))]
    [NotifyPropertyChangedFor(nameof(CursorKind))]
    private TrackerSyncState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    [NotifyPropertyChangedFor(nameof(IconForeground))]
    [NotifyPropertyChangedFor(nameof(BackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(BorderBrush))]
    [NotifyPropertyChangedFor(nameof(IsClickable))]
    [NotifyPropertyChangedFor(nameof(CursorKind))]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _tooltip = string.Empty;

    public bool IsClickable => IsEnabled && State != TrackerSyncState.Pending && State != TrackerSyncState.Success;
    public string CursorKind => IsClickable ? "Hand" : "Arrow";

    public TrackerStatusBadgeVm() { }

    public TrackerStatusBadgeVm(
        string trackerName,
        string displayName,
        TrackerSyncState state,
        string tooltip,
        bool isEnabled,
        Func<Task>? syncAction = null)
    {
        TrackerName = trackerName;
        DisplayName = displayName;
        _state = state;
        _tooltip = tooltip;
        _isEnabled = isEnabled;
        _syncAction = syncAction;
    }

    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncAsync()
    {
        if (!IsClickable) return;
        if (_syncAction != null)
        {
            State = TrackerSyncState.Pending;
            await _syncAction();
        }
    }

    private bool CanSync() => IsClickable;

    partial void OnStateChanged(TrackerSyncState value)
    {
        SyncCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsEnabledChanged(bool value)
    {
        SyncCommand.NotifyCanExecuteChanged();
    }

    public void Update(TrackerSyncState state, string tooltip, bool isEnabled = true)
    {
        IsEnabled = isEnabled;
        Tooltip = tooltip;
        State = state;
    }

    public string IconKind => State switch
    {
        TrackerSyncState.Success => "CheckCircle",
        TrackerSyncState.Retrying => "ClockAlertOutline",
        TrackerSyncState.Pending => "ClockOutline",
        TrackerSyncState.Failed => "CloseCircle",
        TrackerSyncState.NotSynced => IsEnabled ? "CloudUploadOutline" : "CloudOffOutline",
        _ => "HelpCircleOutline"
    };

    public string IconForeground => State switch
    {
        TrackerSyncState.Success => "#2ecc71",
        TrackerSyncState.Retrying => "#f39c12",
        TrackerSyncState.Pending => "#a0a0a0",
        TrackerSyncState.Failed => "#ff5555",
        TrackerSyncState.NotSynced => IsEnabled ? "#3498db" : "#888888",
        _ => "#888888"
    };

    public string BackgroundBrush => State switch
    {
        TrackerSyncState.Success => "#202ecc71",
        TrackerSyncState.Retrying => "#25f39c12",
        TrackerSyncState.Pending => "#1aa0a0a0",
        TrackerSyncState.Failed => "#26ff5555",
        TrackerSyncState.NotSynced => IsEnabled ? "#203498db" : "#15888888",
        _ => "#1A888888"
    };

    public string BorderBrush => State switch
    {
        TrackerSyncState.Success => "#552ecc71",
        TrackerSyncState.Retrying => "#60f39c12",
        TrackerSyncState.Pending => "#40a0a0a0",
        TrackerSyncState.Failed => "#70ff5555",
        TrackerSyncState.NotSynced => IsEnabled ? "#553498db" : "#35888888",
        _ => "#33888888"
    };
}

