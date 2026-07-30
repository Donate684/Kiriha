using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel
{
    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            SetProperty(ref _searchQuery, value);
            _searchDebouncer?.Invoke();
        }
    }
    private Kiriha.Utils.Async.Debouncer? _searchDebouncer;
    private Kiriha.Utils.Async.Debouncer? _filterRefreshDebouncer;
    private int _filterRefreshVersion;

    // Sorting
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySortBy))]
    private string _sortBy = "Title";
    public string DisplaySortBy => UIUtils.GetLoc("filters.sort." + SortBy.ToLower());
    public System.Collections.Generic.List<string> SortOptions { get; } = new() { "Title", "RussianTitle", "Score", "Progress", "Date", "Popularity" };

    // Filters
    [ObservableProperty] private bool _filterNsfw;
    [ObservableProperty] private bool _isFilterActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWatchingSelected))]
    [NotifyPropertyChangedFor(nameof(IsCompletedSelected))]
    [NotifyPropertyChangedFor(nameof(IsOnHoldSelected))]
    [NotifyPropertyChangedFor(nameof(IsDroppedSelected))]
    [NotifyPropertyChangedFor(nameof(IsPlanToWatchSelected))]
    private UserAnimeStatus _selectedStatus = UserAnimeStatus.Watching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnimeSelected))]
    [NotifyPropertyChangedFor(nameof(IsMangaSelected))]
    private MediaKind _selectedMediaKind = MediaKind.Anime;

    public bool IsAnimeSelected => SelectedMediaKind == MediaKind.Anime;
    public bool IsMangaSelected => SelectedMediaKind == MediaKind.Manga;

    public bool IsWatchingSelected => SelectedStatus == UserAnimeStatus.Watching;
    public bool IsCompletedSelected => SelectedStatus == UserAnimeStatus.Completed;
    public bool IsOnHoldSelected => SelectedStatus == UserAnimeStatus.OnHold;
    public bool IsDroppedSelected => SelectedStatus == UserAnimeStatus.Dropped;
    public bool IsPlanToWatchSelected => SelectedStatus == UserAnimeStatus.PlanToWatch;

    [RelayCommand]
    public void ClearFilters()
    {
        FilterNsfw = false;
        IsFilterActive = false;
    }

    [RelayCommand]
    public async Task SwitchMediaKind(string kindString)
    {
        if (Enum.TryParse<MediaKind>(kindString, true, out var kind))
        {
            SelectedMediaKind = kind;
            await UpdateCountsAsync();
            await ApplyCurrentFiltersAsync();
        }
    }

    [RelayCommand]
    public async Task ToggleMediaKind()
    {
        SelectedMediaKind = SelectedMediaKind == MediaKind.Anime ? MediaKind.Manga : MediaKind.Anime;
        await UpdateCountsAsync();
        await ApplyCurrentFiltersAsync();
    }

    [RelayCommand]
    public async Task Filter(string statusString)
    {
        if (Enum.TryParse<UserAnimeStatus>(statusString, true, out var parsed))
        {
            await FilterByStatusAsync(parsed);
        }
    }

    public async Task FilterByStatusAsync(UserAnimeStatus status)
    {
        SelectedStatus = status;
        await ApplyCurrentFiltersAsync();
    }

    private void ScheduleFilterRefresh()
    {
        _filterRefreshDebouncer?.Invoke();
    }

    private async Task ApplyCurrentFiltersAsync(CancellationToken cancellationToken = default)
    {
        var version = Interlocked.Increment(ref _filterRefreshVersion);
        var status = SelectedStatus;
        var query = SearchQuery;
        var nsfw = FilterNsfw;
        var sort = SortBy;
        var kind = SelectedMediaKind;

        var filtered = await Dispatcher.UIThread.InvokeAsync(() =>
            _listProjection.Query(status, query, nsfw, sort, kind));

        if (cancellationToken.IsCancellationRequested || version != Volatile.Read(ref _filterRefreshVersion))
            return;

        // In-place update of the existing AvaloniaList instead of replacing the
        // reference. Re-assigning FilteredItems would force every binding (and
        // any ItemsRepeater materializing this collection) to detach + rebind
        // the entire visual tree, which is what caused a ~358 ms UI-thread
        // stall right after startup population. Mutating the same instance
        // emits one CollectionChanged(Reset) and lets ItemsRepeater diff in
        // its own incremental pipeline.
        FilteredItems.Clear();
        FilteredItems.AddRange(filtered);
    }

    private async Task UpdateCountsAsync()
    {
        var kind = SelectedMediaKind;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var watching = _listProjection.Count(UserAnimeStatus.Watching, kind);
            var completed = _listProjection.Count(UserAnimeStatus.Completed, kind);
            var onHold = _listProjection.Count(UserAnimeStatus.OnHold, kind);
            var dropped = _listProjection.Count(UserAnimeStatus.Dropped, kind);
            var ptw = _listProjection.Count(UserAnimeStatus.PlanToWatch, kind);

            var watchingLocKey = kind == MediaKind.Manga ? "anime.status.reading" : "anime.status.watching";
            WatchingHeader = UIUtils.GetLoc("filters.header_format", GetLoc(watchingLocKey), watching.ToString());
            CompletedHeader = UIUtils.GetLoc("filters.header_format", GetLoc("anime.status.completed"), completed.ToString());
            OnHoldHeader = UIUtils.GetLoc("filters.header_format", GetLoc("anime.status.on_hold"), onHold.ToString());
            DroppedHeader = UIUtils.GetLoc("filters.header_format", GetLoc("anime.status.dropped"), dropped.ToString());
            PlanToWatchHeader = UIUtils.GetLoc("filters.header_format", GetLoc("anime.status.plan_to_watch"), ptw.ToString());
        });
    }
}
