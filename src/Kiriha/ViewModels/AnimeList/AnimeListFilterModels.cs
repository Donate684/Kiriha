using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Domain.Models.Formats;
using Kiriha.Core.Domain.Models.Genres;

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
