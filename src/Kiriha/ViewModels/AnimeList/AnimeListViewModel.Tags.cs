using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Models.Formats;
using Kiriha.Core.Domain.Models.Genres;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel
{
    // Discord-like Tag System
    public ObservableCollection<GenreDefinition> SelectedGenres { get; } = [];
    public ObservableCollection<FormatDefinition> SelectedFormats { get; } = [];
    public ObservableCollection<SearchTagSuggestionItem> TagSuggestions { get; } = [];
    public ObservableCollection<SearchTagSuggestionItem> GenreSuggestions => TagSuggestions;
    public ObservableCollection<GenreFilterItemViewModel> GenreFilterItems { get; } = [];
    public ObservableCollection<FormatFilterItemViewModel> FormatFilterItems { get; } = [];

    [ObservableProperty] private bool _isSuggestionsOpen;
    [ObservableProperty] private SearchTagSuggestionItem? _selectedSuggestion;

    public bool HasSelectedGenres => SelectedGenres.Count > 0;
    public bool HasSelectedFormats => SelectedFormats.Count > 0;
    public bool HasSelectedTags => HasSelectedGenres || HasSelectedFormats;

    public void InitializeGenreFilterItems()
    {
        if (GenreFilterItems.Count == 0)
        {
            foreach (var g in GenreCatalog.All.OrderBy(g => g.RussianName, StringComparer.CurrentCultureIgnoreCase))
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
        var kinds = GetEffectiveMediaKinds(SelectedMediaKind);
        var nsfw = FilterNsfw;

        TagSuggestions.Clear();

        if (clean.Length == 0)
        {
            foreach (var f in FormatCatalog.All)
            {
                int count = _listProjection.CountFormat(status, kinds, f.Key, nsfw);
                bool isSelected = SelectedFormats.Any(x => string.Equals(x.Key, f.Key, StringComparison.OrdinalIgnoreCase));
                TagSuggestions.Add(new SearchTagSuggestionItem(SearchTagKind.Format, null, f, f.RussianName, f.EnglishName, count, isSelected));
            }
            foreach (var g in GenreCatalog.All.Take(6))
            {
                int count = _listProjection.CountGenre(status, kinds, g.Key, nsfw);
                bool isSelected = SelectedGenres.Any(x => string.Equals(x.Key, g.Key, StringComparison.OrdinalIgnoreCase));
                TagSuggestions.Add(new SearchTagSuggestionItem(SearchTagKind.Genre, g, null, g.RussianName, g.EnglishName, count, isSelected));
            }
            IsSuggestionsOpen = TagSuggestions.Count > 0;
            return;
        }

        var matchedFormats = FormatCatalog.SearchFormats(clean, 4);
        foreach (var f in matchedFormats)
        {
            int count = _listProjection.CountFormat(status, kinds, f.Key, nsfw);
            bool isSelected = SelectedFormats.Any(x => string.Equals(x.Key, f.Key, StringComparison.OrdinalIgnoreCase));
            TagSuggestions.Add(new SearchTagSuggestionItem(SearchTagKind.Format, null, f, f.RussianName, f.EnglishName, count, isSelected));
        }

        int remainingSlots = Math.Max(3, 8 - matchedFormats.Count);
        var matchedGenres = GenreCatalog.SearchGenres(clean, remainingSlots);
        foreach (var g in matchedGenres)
        {
            int count = _listProjection.CountGenre(status, kinds, g.Key, nsfw);
            bool isSelected = SelectedGenres.Any(x => string.Equals(x.Key, g.Key, StringComparison.OrdinalIgnoreCase));
            TagSuggestions.Add(new SearchTagSuggestionItem(SearchTagKind.Genre, g, null, g.RussianName, g.EnglishName, count, isSelected));
        }

        IsSuggestionsOpen = TagSuggestions.Count > 0;
    }
}
