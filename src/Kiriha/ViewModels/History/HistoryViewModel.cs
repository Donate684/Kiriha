using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Dialogs;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Services.Data.Core;
using Kiriha.Utils.Async;

namespace Kiriha.ViewModels.History;

public partial class HistoryViewModel : ViewModelBase, IDisposable
{
    private readonly IHistoryService _historyService;
    private readonly DatabaseInitializer _dbInit;
    private readonly IAnimeRepository _animeRepo;
    private readonly IMalApiService _malApi;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _localizer;
    private readonly ISyncManager? _syncManager;
    private readonly IReadOnlyList<ITrackerService> _trackers;
    private readonly INotificationService? _notificationService;
    private readonly IUiDispatcher? _uiDispatcher;
    private readonly ISettingsService? _settingsService;
    private readonly IMetadataRepository? _metadataRepo;
    private List<HistoryItem> _rawItems = new();

    [ObservableProperty]
    private AvaloniaList<HistoryTimelineItem> _timelineItems = new();

    [ObservableProperty]
    private bool _hasHistory;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>0=All, 1=Today, 2=Week, 3=Month</summary>
    [ObservableProperty]
    private int _selectedPeriod;

    /// <summary>0=All, or one of HistoryItem.ActionType values.</summary>
    [ObservableProperty]
    private int _selectedAction;

    public HistoryViewModel(
        IHistoryService historyService,
        DatabaseInitializer dbInit,
        IAnimeRepository animeRepo,
        IMalApiService malApi,
        IDialogService dialogs,
        ILocalizer localizer,
        ISyncManager? syncManager = null,
        IEnumerable<ITrackerService>? trackers = null,
        INotificationService? notificationService = null,
        IUiDispatcher? uiDispatcher = null,
        ISettingsService? settingsService = null,
        IMetadataRepository? metadataRepo = null)
    {
        _historyService = historyService;
        _dbInit = dbInit;
        _animeRepo = animeRepo;
        _malApi = malApi;
        _dialogs = dialogs;
        _localizer = localizer;
        _syncManager = syncManager;
        _trackers = (trackers ?? []).ToList();
        _notificationService = notificationService;
        _uiDispatcher = uiDispatcher;
        _settingsService = settingsService;
        _metadataRepo = metadataRepo;

        _historyService.TrackerStatusUpdated += OnTrackerStatusUpdated;
        _historyService.EntryAdded += OnHistoryEntryAdded;

        RefreshHistory().SafeFireAndForget("HistoryInit");
    }

    private void OnTrackerStatusUpdated(int animeId, int? episode, string trackerName, TrackerSyncState state, string? error)
    {
        RunOnUi(() =>
        {
            try
            {
                var rawMatches = _rawItems.Where(h => h.AnimeId == animeId);
                if (episode.HasValue && episode.Value > 0)
                    rawMatches = rawMatches.Where(h => h.Episode == episode.Value);
                foreach (var raw in rawMatches)
                {
                    raw.SetTrackerStatus(trackerName, state, error);
                }

                bool updatedAny = false;
                foreach (var timelineItem in TimelineItems)
                {
                    if (timelineItem is HistoryEntryVm entry && entry.AnimeId == animeId)
                    {
                        bool matchesEpisode = (episode.HasValue && episode.Value > 0)
                            ? (entry.EpisodeFrom <= episode.Value && episode.Value <= entry.EpisodeTo)
                            : !updatedAny;

                        if (matchesEpisode)
                        {
                            entry.UpdateTrackerStatus(trackerName, state, error, IsTrackerEnabled(trackerName));
                            updatedAny = true;
                            if (!episode.HasValue || episode.Value == 0)
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "HistoryViewModel: Error handling TrackerStatusUpdated");
            }
        });
    }

    private void RunOnUi(Action action)
    {
        if (_uiDispatcher != null)
        {
            _uiDispatcher.Post(action);
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(action);
        }
    }

    private void OnHistoryEntryAdded(Kiriha.Core.Domain.Models.HistoryItem item)
    {
        RunOnUi(() =>
        {
            if (string.IsNullOrEmpty(item.PosterUrl))
            {
                // Resolve poster from the user's local anime collection (cheap, no I/O).
                try
                {
                    var collection = _animeRepo.Collection;
                    var anime = collection.FirstOrDefault(x => x.Id == item.AnimeId);
                    if (anime != null && !string.IsNullOrEmpty(anime.MainPictureUrl))
                        item.PosterUrl = anime.MainPictureUrl;
                }
                catch { }
            }

            // Avoid duplicates: if the entry is already in _rawItems (e.g. from a previous RefreshHistory), skip.
            if (_rawItems.Any(r => r.Id == item.Id && item.Id != 0))
                return;

            _rawItems.Insert(0, item);
            ApplyFilters();
        });
    }

    public IReadOnlyList<string> GetOrderedActiveTrackers()
    {
        // 1. Check enabled status for candidates
        bool isMalEnabled = IsTrackerEnabled(TrackerConstants.Names.Mal);
        bool isAniListEnabled = IsTrackerEnabled(TrackerConstants.Names.AniList);

        // For Shikimori: determine active mirror and its enabled status
        string activeShikiName = TrackerConstants.Names.ShikiOrig;
        string activeShikiId = TrackerConstants.Ids.ShikiOrig;

        if (_settingsService != null)
        {
            bool isFork = _settingsService.Current.Api.ShikiMirror == ShikiMirror.Net;
            activeShikiName = isFork ? TrackerConstants.Names.ShikiFork : TrackerConstants.Names.ShikiOrig;
            activeShikiId = isFork ? TrackerConstants.Ids.ShikiFork : TrackerConstants.Ids.ShikiOrig;
        }
        else
        {
            // Fallback for tests without settings service
            if (_trackers.Any(t => (string.Equals(t.TrackerId, TrackerConstants.Ids.ShikiFork, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(t.Name, TrackerConstants.Names.ShikiFork, StringComparison.OrdinalIgnoreCase)) && t.IsEnabled))
            {
                activeShikiName = TrackerConstants.Names.ShikiFork;
                activeShikiId = TrackerConstants.Ids.ShikiFork;
            }
            else if (_trackers.Any(t => string.Equals(t.Name, TrackerConstants.Names.ShikiGeneral, StringComparison.OrdinalIgnoreCase) && t.IsEnabled))
            {
                activeShikiName = TrackerConstants.Names.ShikiGeneral;
                activeShikiId = TrackerConstants.Ids.ShikiOrig;
            }
        }

        bool isShikiEnabled = IsTrackerEnabled(activeShikiName) || IsTrackerEnabled(activeShikiId);

        // 2. Identify Primary Tracker
        string primaryId = _settingsService?.Current.Api.PrimaryTrackerId ?? TrackerConstants.Ids.Mal;
        string primaryName = TrackerConstants.Names.Mal;
        bool isPrimaryEnabled = false;

        if (string.Equals(primaryId, TrackerConstants.Ids.AniList, StringComparison.OrdinalIgnoreCase))
        {
            primaryName = TrackerConstants.Names.AniList;
            isPrimaryEnabled = isAniListEnabled;
        }
        else if (string.Equals(primaryId, TrackerConstants.Ids.ShikiFork, StringComparison.OrdinalIgnoreCase))
        {
            primaryName = TrackerConstants.Names.ShikiFork;
            isPrimaryEnabled = isShikiEnabled && string.Equals(activeShikiId, TrackerConstants.Ids.ShikiFork, StringComparison.OrdinalIgnoreCase);
        }
        else if (string.Equals(primaryId, TrackerConstants.Ids.ShikiOrig, StringComparison.OrdinalIgnoreCase))
        {
            primaryName = TrackerConstants.Names.ShikiOrig;
            isPrimaryEnabled = isShikiEnabled && string.Equals(activeShikiId, TrackerConstants.Ids.ShikiOrig, StringComparison.OrdinalIgnoreCase);
        }
        else // MAL or fallback
        {
            primaryName = TrackerConstants.Names.Mal;
            isPrimaryEnabled = isMalEnabled;
        }

        // 3. Secondary candidates in requested priority order:
        // AniList, Shikimori (single active mirror), MyAnimeList
        var secondaryCandidates = new List<string>();
        if (isAniListEnabled)
        {
            secondaryCandidates.Add(TrackerConstants.Names.AniList);
        }
        if (isShikiEnabled)
        {
            secondaryCandidates.Add(activeShikiName);
        }
        if (isMalEnabled)
        {
            secondaryCandidates.Add(TrackerConstants.Names.Mal);
        }

        // 4. Assemble: Primary is ALWAYS FIRST (if enabled/logged-in), followed by remaining secondary
        var ordered = new List<string>();
        if (isPrimaryEnabled)
        {
            ordered.Add(primaryName);
        }

        foreach (var candidate in secondaryCandidates)
        {
            if (!string.Equals(candidate, primaryName, StringComparison.OrdinalIgnoreCase))
            {
                ordered.Add(candidate);
            }
        }

        return ordered;
    }

    public bool IsTrackerEnabled(string trackerName)
    {
        if (_trackers.Count == 0 && _settingsService == null) return true;

        // Try matching tracker by Name or TrackerId in registered services
        var tracker = _trackers.FirstOrDefault(t =>
            string.Equals(t.Name, trackerName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.TrackerId, trackerName, StringComparison.OrdinalIgnoreCase));

        if (tracker != null)
        {
            return tracker.IsEnabled;
        }

        // If checking generic "Shikimori", resolve to the active mirror tracker
        if (string.Equals(trackerName, TrackerConstants.Names.ShikiGeneral, StringComparison.OrdinalIgnoreCase))
        {
            if (_settingsService != null)
            {
                var targetId = _settingsService.Current.Api.ShikiMirror == ShikiMirror.Net
                    ? TrackerConstants.Ids.ShikiFork
                    : TrackerConstants.Ids.ShikiOrig;
                var shiki = _trackers.FirstOrDefault(t => string.Equals(t.TrackerId, targetId, StringComparison.OrdinalIgnoreCase));
                if (shiki != null) return shiki.IsEnabled;
            }
            else
            {
                var shiki = _trackers.FirstOrDefault(t => t.Name.StartsWith("Shiki", StringComparison.OrdinalIgnoreCase));
                if (shiki != null) return shiki.IsEnabled;
            }
        }

        // Fallback to checking settings accounts if tracker service not registered in _trackers
        if (_settingsService != null)
        {
            var api = _settingsService.Current.Api;
            if (string.Equals(trackerName, TrackerConstants.Names.Mal, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trackerName, TrackerConstants.Ids.Mal, StringComparison.OrdinalIgnoreCase))
            {
                var acc = api.GetAccount(TrackerConstants.Ids.Mal);
                return (acc != null && acc.IsEnabled && acc.Tokens != null) || api.Mal != null;
            }
            if (string.Equals(trackerName, TrackerConstants.Names.AniList, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trackerName, TrackerConstants.Ids.AniList, StringComparison.OrdinalIgnoreCase))
            {
                var acc = api.GetAccount(TrackerConstants.Ids.AniList);
                return acc != null && acc.IsEnabled && acc.Tokens != null;
            }
            if (string.Equals(trackerName, TrackerConstants.Names.ShikiFork, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trackerName, TrackerConstants.Ids.ShikiFork, StringComparison.OrdinalIgnoreCase))
            {
                var acc = api.GetAccount(TrackerConstants.Ids.ShikiFork);
                return (acc != null && acc.IsEnabled && acc.Tokens != null) ||
                       (api.Shiki != null && api.Shiki.Mirror == ShikiMirror.Net);
            }
            if (string.Equals(trackerName, TrackerConstants.Names.ShikiOrig, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trackerName, TrackerConstants.Ids.ShikiOrig, StringComparison.OrdinalIgnoreCase))
            {
                var acc = api.GetAccount(TrackerConstants.Ids.ShikiOrig);
                return (acc != null && acc.IsEnabled && acc.Tokens != null) ||
                       (api.Shiki != null && api.Shiki.Mirror == ShikiMirror.One);
            }
            if (string.Equals(trackerName, TrackerConstants.Names.ShikiGeneral, StringComparison.OrdinalIgnoreCase))
            {
                return api.Shiki != null;
            }
        }

        return false;
    }



    partial void OnSearchQueryChanged(string value) => ApplyFilters();
    partial void OnSelectedPeriodChanged(int value)
    {
        ApplyFilters();
        NotifyPeriodFlags();
    }
    partial void OnSelectedActionChanged(int value)
    {
        ApplyFilters();
        NotifyActionFlags();
    }

    // Radio-button friendly flags (also safe for ToggleButton: can't uncheck)
    public bool IsPeriodAll { get => SelectedPeriod == 0; set { if (value) SelectedPeriod = 0; else OnPropertyChanged(nameof(IsPeriodAll)); } }
    public bool IsPeriodToday { get => SelectedPeriod == 1; set { if (value) SelectedPeriod = 1; else OnPropertyChanged(nameof(IsPeriodToday)); } }
    public bool IsPeriodWeek { get => SelectedPeriod == 2; set { if (value) SelectedPeriod = 2; else OnPropertyChanged(nameof(IsPeriodWeek)); } }
    public bool IsPeriodMonth { get => SelectedPeriod == 3; set { if (value) SelectedPeriod = 3; else OnPropertyChanged(nameof(IsPeriodMonth)); } }

    public bool IsActionAll { get => SelectedAction == 0; set { if (value) SelectedAction = 0; } }
    public bool IsActionWatched { get => SelectedAction == 1; set { if (value) SelectedAction = 1; } }
    public bool IsActionCompleted { get => SelectedAction == 6; set { if (value) SelectedAction = 6; } }
    public bool IsActionDropped { get => SelectedAction == 7; set { if (value) SelectedAction = 7; } }
    public bool IsActionScoreSet { get => SelectedAction == 5; set { if (value) SelectedAction = 5; } }
    public bool IsActionDeleted { get => SelectedAction == 8; set { if (value) SelectedAction = 8; } }

    private void NotifyPeriodFlags()
    {
        OnPropertyChanged(nameof(IsPeriodAll));
        OnPropertyChanged(nameof(IsPeriodToday));
        OnPropertyChanged(nameof(IsPeriodWeek));
        OnPropertyChanged(nameof(IsPeriodMonth));
    }

    private void NotifyActionFlags()
    {
        OnPropertyChanged(nameof(IsActionAll));
        OnPropertyChanged(nameof(IsActionWatched));
        OnPropertyChanged(nameof(IsActionCompleted));
        OnPropertyChanged(nameof(IsActionDropped));
        OnPropertyChanged(nameof(IsActionScoreSet));
        OnPropertyChanged(nameof(IsActionDeleted));
    }

    public void Dispose()
    {
        _historyService.TrackerStatusUpdated -= OnTrackerStatusUpdated;
        _historyService.EntryAdded -= OnHistoryEntryAdded;
    }
}
