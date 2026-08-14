using Kiriha.Core.Domain.Constants;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Infrastructure;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.ViewModels.Seasonal;

public partial class SeasonalViewModel
{
    private List<AnimeEntity> _allSeasonalItems = new();
    private Dictionary<int, UserAnimeStatus> _userAnimeStore = new();
    private HashSet<int> _hiddenSeasonalIds = new();
    private static readonly ConcurrentDictionary<(int, string), List<AnimeEntity>> _seasonalCache = new();
    private static int _diskHydrated;
    private bool _isInitializing = true;

    private CancellationTokenSource? _loadCts;
    private bool _isDisposed;
    private readonly Kiriha.Utils.Async.Debouncer _filterDebouncer;
    private readonly Kiriha.Utils.Async.Debouncer _applyFilterDebouncer;
    private int _applyFiltersRequestCount;
    private int _initialLoadStarted;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySeason))]
    private int _currentYear;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySeason))]
    private string _currentSeason = "";

    public string DisplaySeason => _localizer.GetLoc("anime.seasons." + CurrentSeason.ToLower());

    [ObservableProperty] private AvaloniaList<AnimeEntity> _displayItems = new();
    [ObservableProperty] private string _currentHeader = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySortBy))]
    private string _sortBy = "";

    public string DisplaySortBy => _localizer.GetLoc("filters.sort." + SortBy.ToLower());

    public List<string> SortOptions { get; } = new()
    {
        AppConstants.Sorting.Popularity,
        AppConstants.Sorting.Score,
        AppConstants.Sorting.Title,
        AppConstants.Sorting.RussianTitle,
        AppConstants.Sorting.Date
    };

    [ObservableProperty] private string? _searchQuery;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewSelected))]
    [NotifyPropertyChangedFor(nameof(IsContinuingSelected))]
    [NotifyPropertyChangedFor(nameof(IsMoviesSelected))]
    [NotifyPropertyChangedFor(nameof(IsOnaSelected))]
    [NotifyPropertyChangedFor(nameof(IsOvaSelected))]
    [NotifyPropertyChangedFor(nameof(IsSpecialsSelected))]
    [NotifyPropertyChangedFor(nameof(IsOtherSelected))]
    private string _selectedCategory = "New";

    public bool IsNewSelected => SelectedCategory == "New";
    public bool IsContinuingSelected => SelectedCategory == "Continuing";
    public bool IsMoviesSelected => SelectedCategory == "Movies";
    public bool IsOnaSelected => SelectedCategory == "ONA";
    public bool IsOvaSelected => SelectedCategory == "OVA";
    public bool IsSpecialsSelected => SelectedCategory == "Specials";
    public bool IsOtherSelected => SelectedCategory == "Other";

    [ObservableProperty] private bool _filterNotInList;
    [ObservableProperty] private bool _filterWatching;
    [ObservableProperty] private bool _filterCompleted;
    [ObservableProperty] private bool _filterOnHold;
    [ObservableProperty] private bool _filterPlanToWatch;
    [ObservableProperty] private bool _filterDropped;
    [ObservableProperty] private bool _filterNsfw;
    [ObservableProperty] private bool _showHidden;

    [ObservableProperty] private bool _isFilterActive;

    [ObservableProperty] private string _newHeader = "";
    [ObservableProperty] private string _continuingHeader = "";
    [ObservableProperty] private string _moviesHeader = "";
    [ObservableProperty] private string _ovaHeader = "";
    [ObservableProperty] private string _onaHeader = "";
    [ObservableProperty] private string _specialsHeader = "";
    [ObservableProperty] private string _otherHeader = "";

    public List<string> Seasons { get; } = new()
    {
        AppConstants.Seasons.Winter,
        AppConstants.Seasons.Spring,
        AppConstants.Seasons.Summer,
        AppConstants.Seasons.Fall
    };
}
