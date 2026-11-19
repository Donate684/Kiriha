using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Core.Domain.Collections;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Utils.Async;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel
{
    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                OnPropertyChanged(nameof(HasSearchOrFilters));
                UpdateTagSuggestions(value);
                _searchDebouncer?.Invoke();
            }
        }
    }

    private Debouncer? _searchDebouncer;
    private Debouncer? _filterRefreshDebouncer;
    private int _filterRefreshVersion;

    public bool HasSearchOrFilters => !string.IsNullOrEmpty(SearchQuery) || HasSelectedTags;

    // Sorting
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySortBy))]
    [NotifyPropertyChangedFor(nameof(IsDateSort))]
    [NotifyPropertyChangedFor(nameof(IsScoreSort))]
    [NotifyPropertyChangedFor(nameof(IsPopularitySort))]
    private string _sortBy = "Title";
    public string DisplaySortBy => _localizer.GetLoc("filters.sort." + SortBy.ToLower());
    public bool IsDateSort => string.Equals(SortBy, AppConstants.Sorting.Date, StringComparison.OrdinalIgnoreCase);
    public bool IsScoreSort => string.Equals(SortBy, AppConstants.Sorting.Score, StringComparison.OrdinalIgnoreCase);
    public bool IsPopularitySort => string.Equals(SortBy, AppConstants.Sorting.Popularity, StringComparison.OrdinalIgnoreCase);
    public System.Collections.Generic.List<string> SortOptions { get; } = ["Title", "RussianTitle", "Score", "Progress", "Date", "Popularity"];
    [ObservableProperty] private bool _prioritizeNewEpisodes;
    [ObservableProperty] private bool _prioritizeUnwatched;

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
    public async Task SwitchMediaKind(string kindString)
    {
        if (Enum.TryParse<MediaKind>(kindString, true, out var kind))
        {
            if (SelectedMediaKind == kind) return;
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

    private static readonly MediaKind[] AnimeTabKinds = [MediaKind.Anime];
    private static readonly MediaKind[] MangaTabKinds = [MediaKind.Manga, MediaKind.LightNovel];

    private static MediaKind[] GetEffectiveMediaKinds(MediaKind kind) =>
        kind == MediaKind.Manga ? MangaTabKinds : AnimeTabKinds;

    private async Task ApplyCurrentFiltersAsync(CancellationToken cancellationToken = default)
    {
        var version = Interlocked.Increment(ref _filterRefreshVersion);
        var status = SelectedStatus;
        var query = SearchQuery;
        var activeGenreKeys = SelectedGenres.Select(g => g.Key).ToList();
        var activeFormatKeys = SelectedFormats.Select(f => f.Key).ToList();
        var nsfw = FilterNsfw;
        var sort = SortBy;
        var kind = SelectedMediaKind;
        var kinds = GetEffectiveMediaKinds(kind);
        var prioritizeNewEpisodes = PrioritizeNewEpisodes;
        var prioritizeUnwatched = PrioritizeUnwatched;

        var (filtered, counts) = await Task.Run(() =>
        {
            var items = _listProjection.Query(status, query, activeGenreKeys, activeFormatKeys, nsfw, sort, kinds, prioritizeNewEpisodes, prioritizeUnwatched);
            var statusCounts = _listProjection.CountAllStatuses(query, activeGenreKeys, activeFormatKeys, nsfw, kinds);
            return (items, statusCounts);
        }, cancellationToken);

        if (cancellationToken.IsCancellationRequested || version != Volatile.Read(ref _filterRefreshVersion))
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested || version != Volatile.Read(ref _filterRefreshVersion))
                return;

            FilteredItems.Clear();
            FilteredItems.AddRange(filtered);
            ApplyStatusCounts(counts, kind);
        });
    }

    private void ApplyStatusCounts(AnimeStatusCounts counts, MediaKind kind)
    {
        var watchingLocKey = kind == MediaKind.Manga ? "anime.status.reading" : "anime.status.watching";
        WatchingHeader = _localizer.GetLoc("filters.header_format", GetLoc(watchingLocKey), counts.Watching.ToString());
        CompletedHeader = _localizer.GetLoc("filters.header_format", GetLoc("anime.status.completed"), counts.Completed.ToString());
        OnHoldHeader = _localizer.GetLoc("filters.header_format", GetLoc("anime.status.on_hold"), counts.OnHold.ToString());
        DroppedHeader = _localizer.GetLoc("filters.header_format", GetLoc("anime.status.dropped"), counts.Dropped.ToString());
        PlanToWatchHeader = _localizer.GetLoc("filters.header_format", GetLoc("anime.status.plan_to_watch"), counts.PlanToWatch.ToString());
    }

    private async Task UpdateCountsAsync()
    {
        var kind = SelectedMediaKind;
        var kinds = GetEffectiveMediaKinds(kind);
        var query = SearchQuery;
        var activeGenreKeys = SelectedGenres.Select(g => g.Key).ToList();
        var activeFormatKeys = SelectedFormats.Select(f => f.Key).ToList();
        var nsfw = FilterNsfw;

        var counts = await Task.Run(() =>
            _listProjection.CountAllStatuses(query, activeGenreKeys, activeFormatKeys, nsfw, kinds));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ApplyStatusCounts(counts, kind);
        });
    }
}
