using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
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

    public bool CanTrack => AnimeId > 0 && (ActionType == 1 || ActionType == 4 || ActionType == 5 || ActionType == 6 || ActionType == 7 || ActionType == 8);

    public ObservableCollection<TrackerStatusBadgeVm> TrackerBadges { get; } = new();
    public bool HasTrackerBadges => CanTrack && TrackerBadges.Count > 0;

    private Func<string, Task>? _onSync;
    private Func<string, bool>? _isTrackerEnabled;

    public void LoadTrackerStatuses(
        Dictionary<string, TrackerSyncInfo> statuses,
        Func<string, Task>? onSync = null,
        Func<string, bool>? isTrackerEnabled = null,
        IEnumerable<string>? orderedTrackers = null)
    {
        _onSync = onSync;
        _isTrackerEnabled = isTrackerEnabled;

        TrackerBadges.Clear();
        if (!CanTrack)
        {
            OnPropertyChanged(nameof(HasTrackerBadges));
            return;
        }

        List<string> trackersToDisplay;
        if (orderedTrackers != null)
        {
            trackersToDisplay = orderedTrackers.ToList();
        }
        else
        {
            // Legacy / direct fallback: only include enabled trackers
            var trackerNames = new List<string>();
            if (_isTrackerEnabled == null || _isTrackerEnabled(TrackerConstants.Names.ShikiGeneral))
                trackerNames.Add(TrackerConstants.Names.ShikiGeneral);
            if (_isTrackerEnabled == null || _isTrackerEnabled(TrackerConstants.Names.Mal))
                trackerNames.Add(TrackerConstants.Names.Mal);

            foreach (var key in statuses.Keys)
            {
                if (!trackerNames.Any(t => IsMatchingTracker(t, key)) && (_isTrackerEnabled == null || _isTrackerEnabled(key)))
                {
                    trackerNames.Add(key);
                }
            }
            trackersToDisplay = trackerNames;
        }

        foreach (var trackerName in trackersToDisplay)
        {
            bool isEnabled = _isTrackerEnabled == null || _isTrackerEnabled(trackerName);
            // Strict rule: if the tracker is not logged in / not enabled, no badge is displayed at all.
            if (!isEnabled) continue;

            var shortName = GetDisplayName(trackerName);
            var state = TrackerSyncState.NotSynced;
            string? error = null;

            var syncInfo = FindSyncInfo(statuses, trackerName);
            if (syncInfo != null)
            {
                state = syncInfo.State;
                error = syncInfo.ErrorMessage;
            }

            string tooltip = FormatTooltip(trackerName, state, error, true);

            TrackerBadges.Add(new TrackerStatusBadgeVm(
                trackerName,
                shortName,
                state,
                tooltip,
                true,
                _onSync != null ? () => _onSync(trackerName) : null));
        }

        OnPropertyChanged(nameof(HasTrackerBadges));
    }

    public void UpdateTrackerStatus(string trackerName, TrackerSyncState state, string? error, bool isEnabled = true)
    {
        Primary?.SetTrackerStatus(trackerName, state, error);

        var existing = TrackerBadges.FirstOrDefault(b => IsMatchingTracker(b.TrackerName, trackerName));
        if (existing != null)
        {
            string tooltip = FormatTooltip(existing.TrackerName, state, error, isEnabled);
            existing.Update(state, tooltip, isEnabled);
        }
        else if (CanTrack && isEnabled)
        {
            var shortName = GetDisplayName(trackerName);
            string tooltip = FormatTooltip(trackerName, state, error, isEnabled);
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

    public static bool IsMatchingTracker(string t1, string t2)
    {
        if (string.Equals(t1, t2, StringComparison.OrdinalIgnoreCase)) return true;

        if (IsShiki(t1) && IsShiki(t2)) return true;
        if (IsMal(t1) && IsMal(t2)) return true;
        if (IsAniList(t1) && IsAniList(t2)) return true;

        return false;
    }

    private static bool IsShiki(string name) =>
        name.StartsWith("Shiki", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("shiki-", StringComparison.OrdinalIgnoreCase);

    private static bool IsMal(string name) =>
        string.Equals(name, TrackerConstants.Names.Mal, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, TrackerConstants.Ids.Mal, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "MAL", StringComparison.OrdinalIgnoreCase);

    private static bool IsAniList(string name) =>
        string.Equals(name, TrackerConstants.Names.AniList, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, TrackerConstants.Ids.AniList, StringComparison.OrdinalIgnoreCase);

    private static TrackerSyncInfo? FindSyncInfo(Dictionary<string, TrackerSyncInfo> statuses, string trackerName)
    {
        if (statuses.TryGetValue(trackerName, out var directInfo))
        {
            return directInfo;
        }

        var match = statuses.FirstOrDefault(k => IsMatchingTracker(k.Key, trackerName));
        return match.Value;
    }

    private static string GetDisplayName(string trackerName)
    {
        if (IsMal(trackerName))
        {
            return "MAL";
        }
        if (string.Equals(trackerName, TrackerConstants.Names.ShikiGeneral, StringComparison.OrdinalIgnoreCase))
        {
            return "Shiki";
        }
        if (string.Equals(trackerName, TrackerConstants.Names.ShikiOrig, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trackerName, TrackerConstants.Ids.ShikiOrig, StringComparison.OrdinalIgnoreCase))
        {
            return TrackerConstants.Names.ShikiOrig;
        }
        if (string.Equals(trackerName, TrackerConstants.Names.ShikiFork, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trackerName, TrackerConstants.Ids.ShikiFork, StringComparison.OrdinalIgnoreCase))
        {
            return TrackerConstants.Names.ShikiFork;
        }
        if (IsAniList(trackerName))
        {
            return TrackerConstants.Names.AniList;
        }
        return trackerName;
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

