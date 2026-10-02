using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Dialogs;
using Kiriha.Core.Domain.Models;
using Kiriha.Services.Data.Core;
using Kiriha.Utils.Async;

namespace Kiriha.ViewModels.History;

public partial class HistoryViewModel : ViewModelBase
{
    private readonly HistoryService _historyService;
    private readonly DatabaseInitializer _dbInit;
    private readonly IAnimeRepository _animeRepo;
    private readonly IMalApiService _malApi;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _localizer;
    private readonly ISyncManager? _syncManager;
    private readonly IReadOnlyList<ITrackerService> _trackers;
    private readonly INotificationService? _notificationService;
    private readonly IUiDispatcher? _uiDispatcher;
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
        HistoryService historyService,
        DatabaseInitializer dbInit,
        IAnimeRepository animeRepo,
        IMalApiService malApi,
        IDialogService dialogs,
        ILocalizer localizer,
        ISyncManager? syncManager = null,
        IEnumerable<ITrackerService>? trackers = null,
        INotificationService? notificationService = null,
        IUiDispatcher? uiDispatcher = null)
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

        _historyService.TrackerStatusUpdated += (animeId, episode, trackerName, state, error) =>
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
        };

        // Live update: add new entries to the timeline as they are committed to DB.
        _historyService.EntryAdded += OnHistoryEntryAdded;

        RefreshHistory().SafeFireAndForget("HistoryInit");
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
            // Resolve poster from the user's local anime collection (cheap, no I/O).
            try
            {
                var collection = _animeRepo.Collection;
                var anime = collection.FirstOrDefault(x => x.Id == item.AnimeId);
                if (anime != null) item.PosterUrl = anime.MainPictureUrl;
            }
            catch { }

            // Avoid duplicates: if the entry is already in _rawItems (e.g. from a previous RefreshHistory), skip.
            if (_rawItems.Any(r => r.Id == item.Id && item.Id != 0))
                return;

            _rawItems.Insert(0, item);
            ApplyFilters();
        });
    }

    public bool IsTrackerEnabled(string trackerName)
    {
        if (_trackers.Count == 0) return true;
        var tracker = _trackers.FirstOrDefault(t => string.Equals(t.Name, trackerName, StringComparison.OrdinalIgnoreCase));
        return tracker == null || tracker.IsEnabled;
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

    // â”€â”€â”€ Radio-button friendly flags (also safe for ToggleButton: can't uncheck) â”€â”€â”€
    public bool IsPeriodAll { get => SelectedPeriod == 0; set { if (value) SelectedPeriod = 0; else OnPropertyChanged(nameof(IsPeriodAll)); } }
    public bool IsPeriodToday { get => SelectedPeriod == 1; set { if (value) SelectedPeriod = 1; else OnPropertyChanged(nameof(IsPeriodToday)); } }
    public bool IsPeriodWeek { get => SelectedPeriod == 2; set { if (value) SelectedPeriod = 2; else OnPropertyChanged(nameof(IsPeriodWeek)); } }
    public bool IsPeriodMonth { get => SelectedPeriod == 3; set { if (value) SelectedPeriod = 3; else OnPropertyChanged(nameof(IsPeriodMonth)); } }

    public bool IsActionAll { get => SelectedAction == 0; set { if (value) SelectedAction = 0; } }
    public bool IsActionWatched { get => SelectedAction == 1; set { if (value) SelectedAction = 1; } }
    public bool IsActionCompleted { get => SelectedAction == 6; set { if (value) SelectedAction = 6; } }
    public bool IsActionDropped { get => SelectedAction == 7; set { if (value) SelectedAction = 7; } }
    public bool IsActionScoreSet { get => SelectedAction == 5; set { if (value) SelectedAction = 5; } }

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
    }
}
