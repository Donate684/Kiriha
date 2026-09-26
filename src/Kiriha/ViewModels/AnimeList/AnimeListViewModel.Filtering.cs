using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Models.Formats;
using Kiriha.Core.Domain.Models.Genres;
using Kiriha.Infrastructure;
using Kiriha.Models;

namespace Kiriha.ViewModels.AnimeList;

public sealed partial class GenreFilterItemViewModel : ObservableObject
{
    private readonly Action<GenreFilterItemViewModel>? _onToggled;
    public GenreDefinition Genre { get; }
    public string DisplayName => Genre.RussianName;
    public string EnglishName => Genre.EnglishName;

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        _onToggled?.Invoke(this);
    }

    public GenreFilterItemViewModel(GenreDefinition genre, Action<GenreFilterItemViewModel>? onToggled = null, bool isSelected = false)
    {
        Genre = genre;
        _onToggled = onToggled;
        _isSelected = isSelected;
    }
}

public sealed partial class FormatFilterItemViewModel : ObservableObject
{
    private readonly Action<FormatFilterItemViewModel>? _onToggled;
    public FormatDefinition Format { get; }
    public string DisplayName => Format.RussianName;
    public string EnglishName => Format.EnglishName;

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        _onToggled?.Invoke(this);
    }

    public FormatFilterItemViewModel(FormatDefinition format, Action<FormatFilterItemViewModel>? onToggled = null, bool isSelected = false)
    {
        Format = format;
        _onToggled = onToggled;
        _isSelected = isSelected;
    }
}

public enum SearchTagKind
{
    Genre,
    Format
}

public sealed class SearchTagSuggestionItem : ObservableObject
{
    public SearchTagKind Kind { get; }
    public GenreDefinition? Genre { get; }
    public FormatDefinition? Format { get; }
    public string Name { get; }
    public string Subtitle { get; }
    public int Count { get; }
    public bool IsSelected { get; }
    public bool IsFormat => Kind == SearchTagKind.Format;
    public bool IsGenre => Kind == SearchTagKind.Genre;
    public string DisplayCount => Count > 0 ? Count.ToString() : string.Empty;
    public bool HasCount => Count > 0;

    public SearchTagSuggestionItem(
        SearchTagKind kind,
        GenreDefinition? genre,
        FormatDefinition? format,
        string name,
        string subtitle,
        int count,
        bool isSelected)
    {
        Kind = kind;
        Genre = genre;
        Format = format;
        Name = name;
        Subtitle = subtitle;
        Count = count;
        IsSelected = isSelected;
    }
}

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

    private Kiriha.Utils.Async.Debouncer? _searchDebouncer;
    private Kiriha.Utils.Async.Debouncer? _filterRefreshDebouncer;
    private int _filterRefreshVersion;

    // Discord-like Tag System
    public ObservableCollection<GenreDefinition> SelectedGenres { get; } = new();
    public ObservableCollection<FormatDefinition> SelectedFormats { get; } = new();
    public ObservableCollection<SearchTagSuggestionItem> TagSuggestions { get; } = new();
    public ObservableCollection<SearchTagSuggestionItem> GenreSuggestions => TagSuggestions;
    public ObservableCollection<GenreFilterItemViewModel> GenreFilterItems { get; } = new();
    public ObservableCollection<FormatFilterItemViewModel> FormatFilterItems { get; } = new();

    [ObservableProperty] private bool _isSuggestionsOpen;
    [ObservableProperty] private SearchTagSuggestionItem? _selectedSuggestion;

    public bool HasSelectedGenres => SelectedGenres.Count > 0;
    public bool HasSelectedFormats => SelectedFormats.Count > 0;
    public bool HasSelectedTags => HasSelectedGenres || HasSelectedFormats;
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
    public System.Collections.Generic.List<string> SortOptions { get; } = new() { "Title", "RussianTitle", "Score", "Progress", "Date", "Popularity" };
    [ObservableProperty] private bool _prioritizeNewEpisodes;

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

    public void InitializeGenreFilterItems()
    {
        if (GenreFilterItems.Count == 0)
        {
            foreach (var g in GenreCatalog.All)
            {
                GenreFilterItems.Add(new GenreFilterItemViewModel(g, OnGenreFilterItemToggled));
            }
        }
    }

    public void InitializeFormatFilterItems()
    {
        if (FormatFilterItems.Count == 0)
        {
            foreach (var f in FormatCatalog.All)
            {
                FormatFilterItems.Add(new FormatFilterItemViewModel(f, OnFormatFilterItemToggled));
            }
        }
    }

    private void OnGenreFilterItemToggled(GenreFilterItemViewModel item)
    {
        if (item.IsSelected)
        {
            AddGenreTag(item.Genre);
        }
        else
        {
            RemoveGenreTag(item.Genre);
        }
    }

    private void OnFormatFilterItemToggled(FormatFilterItemViewModel item)
    {
        if (item.IsSelected)
        {
            AddFormatTag(item.Format);
        }
        else
        {
            RemoveFormatTag(item.Format);
        }
    }

    [RelayCommand]
    public void AddGenreTag(GenreDefinition? genre)
    {
        if (genre == null) return;

        if (SelectedGenres.All(g => !string.Equals(g.Key, genre.Key, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedGenres.Add(genre);
            SyncFilterItemState(genre.Key, true);
            OnPropertyChanged(nameof(HasSelectedGenres));
            OnPropertyChanged(nameof(HasSelectedTags));
            OnPropertyChanged(nameof(HasSearchOrFilters));
            IsFilterActive = FilterNsfw || HasSelectedTags;
            RemoveTokenFromSearchQuery(genre);
            IsSuggestionsOpen = false;
            ScheduleFilterRefresh();
        }
        else
        {
            RemoveTokenFromSearchQuery(genre);
            IsSuggestionsOpen = false;
        }
    }

    [RelayCommand]
    public void RemoveGenreTag(GenreDefinition? genre)
    {
        if (genre == null) return;

        var existing = SelectedGenres.FirstOrDefault(g => string.Equals(g.Key, genre.Key, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            SelectedGenres.Remove(existing);
            SyncFilterItemState(genre.Key, false);
            OnPropertyChanged(nameof(HasSelectedGenres));
            OnPropertyChanged(nameof(HasSelectedTags));
            OnPropertyChanged(nameof(HasSearchOrFilters));
            IsFilterActive = FilterNsfw || HasSelectedTags;
            ScheduleFilterRefresh();
        }
    }

    [RelayCommand]
    public void ToggleGenreTag(GenreDefinition? genre)
    {
        if (genre == null) return;

        var existing = SelectedGenres.FirstOrDefault(g => string.Equals(g.Key, genre.Key, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            RemoveGenreTag(existing);
        }
        else
        {
            AddGenreTag(genre);
        }
    }

    public void RemoveLastGenreTag()
    {
        if (SelectedGenres.Count > 0)
        {
            var last = SelectedGenres[^1];
            SelectedGenres.RemoveAt(SelectedGenres.Count - 1);
            SyncFilterItemState(last.Key, false);
            OnPropertyChanged(nameof(HasSelectedGenres));
            OnPropertyChanged(nameof(HasSelectedTags));
            OnPropertyChanged(nameof(HasSearchOrFilters));
            IsFilterActive = FilterNsfw || HasSelectedTags;
            ScheduleFilterRefresh();
        }
    }

    [RelayCommand]
    public void AddFormatTag(FormatDefinition? format)
    {
        if (format == null) return;

        if (SelectedFormats.All(f => !string.Equals(f.Key, format.Key, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedFormats.Add(format);
            SyncFormatFilterItemState(format.Key, true);
            OnPropertyChanged(nameof(HasSelectedFormats));
            OnPropertyChanged(nameof(HasSelectedTags));
            OnPropertyChanged(nameof(HasSearchOrFilters));
            IsFilterActive = FilterNsfw || HasSelectedTags;
            RemoveFormatTokenFromSearchQuery(format);
            IsSuggestionsOpen = false;
            ScheduleFilterRefresh();
        }
        else
        {
            RemoveFormatTokenFromSearchQuery(format);
            IsSuggestionsOpen = false;
        }
    }

    [RelayCommand]
    public void RemoveFormatTag(FormatDefinition? format)
    {
        if (format == null) return;

        var existing = SelectedFormats.FirstOrDefault(f => string.Equals(f.Key, format.Key, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            SelectedFormats.Remove(existing);
            SyncFormatFilterItemState(format.Key, false);
            OnPropertyChanged(nameof(HasSelectedFormats));
            OnPropertyChanged(nameof(HasSelectedTags));
            OnPropertyChanged(nameof(HasSearchOrFilters));
            IsFilterActive = FilterNsfw || HasSelectedTags;
            ScheduleFilterRefresh();
        }
    }

    [RelayCommand]
    public void ToggleFormatTag(FormatDefinition? format)
    {
        if (format == null) return;

        var existing = SelectedFormats.FirstOrDefault(f => string.Equals(f.Key, format.Key, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            RemoveFormatTag(existing);
        }
        else
        {
            AddFormatTag(format);
        }
    }

    public void RemoveLastFormatTag()
    {
        if (SelectedFormats.Count > 0)
        {
            var last = SelectedFormats[^1];
            SelectedFormats.RemoveAt(SelectedFormats.Count - 1);
            SyncFormatFilterItemState(last.Key, false);
            OnPropertyChanged(nameof(HasSelectedFormats));
            OnPropertyChanged(nameof(HasSelectedTags));
            OnPropertyChanged(nameof(HasSearchOrFilters));
            IsFilterActive = FilterNsfw || HasSelectedTags;
            ScheduleFilterRefresh();
        }
    }

    public void RemoveLastTagOrBackspace()
    {
        if (SelectedGenres.Count > 0)
        {
            RemoveLastGenreTag();
        }
        else if (SelectedFormats.Count > 0)
        {
            RemoveLastFormatTag();
        }
    }

    [RelayCommand]
    public void AddTagSuggestion(SearchTagSuggestionItem? suggestion)
    {
        if (suggestion == null) return;

        if (suggestion.IsFormat && suggestion.Format != null)
        {
            AddFormatTag(suggestion.Format);
        }
        else if (suggestion.IsGenre && suggestion.Genre != null)
        {
            AddGenreTag(suggestion.Genre);
        }
    }

    [RelayCommand]
    public void ClearSearch()
    {
        SearchQuery = string.Empty;
        ClearSelectedGenreList();
        ClearSelectedFormatList();
        OnPropertyChanged(nameof(HasSelectedGenres));
        OnPropertyChanged(nameof(HasSelectedFormats));
        OnPropertyChanged(nameof(HasSelectedTags));
        OnPropertyChanged(nameof(HasSearchOrFilters));
        IsFilterActive = FilterNsfw;
        IsSuggestionsOpen = false;
        ScheduleFilterRefresh();
    }

    [RelayCommand]
    public void ClearFilters()
    {
        FilterNsfw = false;
        ClearSelectedGenreList();
        ClearSelectedFormatList();
        OnPropertyChanged(nameof(HasSelectedGenres));
        OnPropertyChanged(nameof(HasSelectedFormats));
        OnPropertyChanged(nameof(HasSelectedTags));
        OnPropertyChanged(nameof(HasSearchOrFilters));
        IsFilterActive = false;
        ScheduleFilterRefresh();
    }

    private void ClearSelectedGenreList()
    {
        SelectedGenres.Clear();
        foreach (var item in GenreFilterItems)
        {
            item.IsSelected = false;
        }
    }

    private void ClearSelectedFormatList()
    {
        SelectedFormats.Clear();
        foreach (var item in FormatFilterItems)
        {
            item.IsSelected = false;
        }
    }

    private void SyncFilterItemState(string key, bool isSelected)
    {
        var item = GenreFilterItems.FirstOrDefault(x => string.Equals(x.Genre.Key, key, StringComparison.OrdinalIgnoreCase));
        if (item != null && item.IsSelected != isSelected)
        {
            item.IsSelected = isSelected;
        }
    }

    private void SyncFormatFilterItemState(string key, bool isSelected)
    {
        var item = FormatFilterItems.FirstOrDefault(x => string.Equals(x.Format.Key, key, StringComparison.OrdinalIgnoreCase));
        if (item != null && item.IsSelected != isSelected)
        {
            item.IsSelected = isSelected;
        }
    }

    private void RemoveTokenFromSearchQuery(GenreDefinition genre)
    {
        if (string.IsNullOrWhiteSpace(_searchQuery)) return;

        var trimmed = _searchQuery.Trim();
        if (trimmed.StartsWith('#'))
        {
            var afterHash = trimmed[1..].Trim();
            if (string.IsNullOrEmpty(afterHash) || MatchesGenreToken(afterHash, genre))
            {
                SearchQuery = string.Empty;
                return;
            }
        }

        if (MatchesGenreToken(trimmed, genre))
        {
            SearchQuery = string.Empty;
            return;
        }

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        int initialCount = words.Count;
        words.RemoveAll(w => MatchesGenreToken(w, genre));

        if (words.Count == initialCount && initialCount == 1)
        {
            SearchQuery = string.Empty;
            return;
        }

        SearchQuery = string.Join(" ", words);
    }

    private static bool MatchesGenreToken(string token, GenreDefinition genre)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var clean = token.Trim();
        if (clean.StartsWith('#'))
        {
            clean = clean[1..];
        }
        if (clean.Length == 0) return true;

        if (GenreCatalog.TryFindGenre(clean, out var g) && g?.Key == genre.Key)
        {
            return true;
        }

        var norm = GenreCatalog.NormalizeLookup(clean);
        if (norm.Length == 0) return false;

        var normKey = GenreCatalog.NormalizeLookup(genre.Key);
        var normRu = GenreCatalog.NormalizeLookup(genre.RussianName);
        var normEn = GenreCatalog.NormalizeLookup(genre.EnglishName);

        if (normRu.StartsWith(norm, StringComparison.OrdinalIgnoreCase) ||
            normEn.StartsWith(norm, StringComparison.OrdinalIgnoreCase) ||
            normKey.StartsWith(norm, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (genre.Aliases != null && genre.Aliases.Any(a => GenreCatalog.NormalizeLookup(a).StartsWith(norm, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (normRu.Contains(norm, StringComparison.OrdinalIgnoreCase) ||
            normEn.Contains(norm, StringComparison.OrdinalIgnoreCase) ||
            normKey.Contains(norm, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (genre.Aliases != null && genre.Aliases.Any(a => GenreCatalog.NormalizeLookup(a).Contains(norm, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private void RemoveFormatTokenFromSearchQuery(FormatDefinition format)
    {
        if (string.IsNullOrWhiteSpace(_searchQuery)) return;

        var trimmed = _searchQuery.Trim();
        if (trimmed.StartsWith('#'))
        {
            var afterHash = trimmed[1..].Trim();
            if (string.IsNullOrEmpty(afterHash) || MatchesFormatToken(afterHash, format))
            {
                SearchQuery = string.Empty;
                return;
            }
        }

        if (MatchesFormatToken(trimmed, format))
        {
            SearchQuery = string.Empty;
            return;
        }

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        int initialCount = words.Count;
        words.RemoveAll(w => MatchesFormatToken(w, format));

        if (words.Count == initialCount && initialCount == 1)
        {
            SearchQuery = string.Empty;
            return;
        }

        SearchQuery = string.Join(" ", words);
    }

    private static bool MatchesFormatToken(string token, FormatDefinition format)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var clean = token.Trim();
        if (clean.StartsWith('#'))
        {
            clean = clean[1..];
        }
        if (clean.Length == 0) return true;

        if (FormatCatalog.TryFindFormat(clean, out var f) && f?.Key == format.Key)
        {
            return true;
        }

        var norm = FormatCatalog.NormalizeLookup(clean);
        if (norm.Length == 0) return false;

        var normKey = FormatCatalog.NormalizeLookup(format.Key);
        var normRu = FormatCatalog.NormalizeLookup(format.RussianName);
        var normEn = FormatCatalog.NormalizeLookup(format.EnglishName);

        if (normRu.StartsWith(norm, StringComparison.OrdinalIgnoreCase) ||
            normEn.StartsWith(norm, StringComparison.OrdinalIgnoreCase) ||
            normKey.StartsWith(norm, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (format.Aliases != null && format.Aliases.Any(a => FormatCatalog.NormalizeLookup(a).StartsWith(norm, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (normRu.Contains(norm, StringComparison.OrdinalIgnoreCase) ||
            normEn.Contains(norm, StringComparison.OrdinalIgnoreCase) ||
            normKey.Contains(norm, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (format.Aliases != null && format.Aliases.Any(a => FormatCatalog.NormalizeLookup(a).Contains(norm, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private void UpdateTagSuggestions(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            TagSuggestions.Clear();
            IsSuggestionsOpen = false;
            return;
        }

        var clean = query.Trim();
        if (clean.StartsWith('#'))
        {
            clean = clean[1..];
        }

        var status = SelectedStatus;
        var kind = SelectedMediaKind;
        var nsfw = FilterNsfw;

        TagSuggestions.Clear();

        if (clean.Length == 0)
        {
            foreach (var f in FormatCatalog.All)
            {
                int count = _listProjection.CountFormat(status, kind, f.Key, nsfw);
                bool isSelected = SelectedFormats.Any(x => string.Equals(x.Key, f.Key, StringComparison.OrdinalIgnoreCase));
                TagSuggestions.Add(new SearchTagSuggestionItem(SearchTagKind.Format, null, f, f.RussianName, f.EnglishName, count, isSelected));
            }
            foreach (var g in GenreCatalog.All.Take(6))
            {
                int count = _listProjection.CountGenre(status, kind, g.Key, nsfw);
                bool isSelected = SelectedGenres.Any(x => string.Equals(x.Key, g.Key, StringComparison.OrdinalIgnoreCase));
                TagSuggestions.Add(new SearchTagSuggestionItem(SearchTagKind.Genre, g, null, g.RussianName, g.EnglishName, count, isSelected));
            }
            IsSuggestionsOpen = TagSuggestions.Count > 0;
            return;
        }

        var matchedFormats = FormatCatalog.SearchFormats(clean, 4);
        foreach (var f in matchedFormats)
        {
            int count = _listProjection.CountFormat(status, kind, f.Key, nsfw);
            bool isSelected = SelectedFormats.Any(x => string.Equals(x.Key, f.Key, StringComparison.OrdinalIgnoreCase));
            TagSuggestions.Add(new SearchTagSuggestionItem(SearchTagKind.Format, null, f, f.RussianName, f.EnglishName, count, isSelected));
        }

        int remainingSlots = Math.Max(3, 8 - matchedFormats.Count);
        var matchedGenres = GenreCatalog.SearchGenres(clean, remainingSlots);
        foreach (var g in matchedGenres)
        {
            int count = _listProjection.CountGenre(status, kind, g.Key, nsfw);
            bool isSelected = SelectedGenres.Any(x => string.Equals(x.Key, g.Key, StringComparison.OrdinalIgnoreCase));
            TagSuggestions.Add(new SearchTagSuggestionItem(SearchTagKind.Genre, g, null, g.RussianName, g.EnglishName, count, isSelected));
        }

        IsSuggestionsOpen = TagSuggestions.Count > 0;
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
        var activeGenreKeys = SelectedGenres.Select(g => g.Key).ToList();
        var activeFormatKeys = SelectedFormats.Select(f => f.Key).ToList();
        var nsfw = FilterNsfw;
        var sort = SortBy;
        var kind = SelectedMediaKind;
        var prioritizeNewEpisodes = PrioritizeNewEpisodes;

        var filtered = await Task.Run(() =>
            _listProjection.Query(status, query, activeGenreKeys, activeFormatKeys, nsfw, sort, kind, prioritizeNewEpisodes), cancellationToken);

        if (cancellationToken.IsCancellationRequested || version != Volatile.Read(ref _filterRefreshVersion))
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested || version != Volatile.Read(ref _filterRefreshVersion))
                return;

            FilteredItems.Clear();
            FilteredItems.AddRange(filtered);
        });
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
            WatchingHeader = _localizer.GetLoc("filters.header_format", GetLoc(watchingLocKey), watching.ToString());
            CompletedHeader = _localizer.GetLoc("filters.header_format", GetLoc("anime.status.completed"), completed.ToString());
            OnHoldHeader = _localizer.GetLoc("filters.header_format", GetLoc("anime.status.on_hold"), onHold.ToString());
            DroppedHeader = _localizer.GetLoc("filters.header_format", GetLoc("anime.status.dropped"), dropped.ToString());
            PlanToWatchHeader = _localizer.GetLoc("filters.header_format", GetLoc("anime.status.plan_to_watch"), ptw.ToString());
        });
    }
}
