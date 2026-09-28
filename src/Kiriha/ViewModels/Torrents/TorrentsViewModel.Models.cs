using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.ViewModels.Torrents;

public enum TorrentSortMode
{
    Newest,
    Matched,
    ReleaseGroup,
}

public sealed class HideableAnimeItem : ObservableObject
{
    public HideableAnimeItem(AnimeEntity anime, bool isHidden)
    {
        Anime = anime;
        _isHidden = isHidden;
    }

    public AnimeEntity Anime { get; }

    private bool _isHidden;
    public bool IsHidden
    {
        get => _isHidden;
        set
        {
            if (SetProperty(ref _isHidden, value))
                HiddenChanged?.Invoke(this);
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public event System.Action<HideableAnimeItem>? HiddenChanged;
}

public sealed class TorrentGroup
{
    public TorrentGroup(string animeTitle, IReadOnlyList<TorrentEntity> items)
    {
        AnimeTitle = animeTitle;
        Items = items;
        HasMatch = items.Any(i => i.IsMatched);
        LatestDate = items.Count > 0 ? items.Max(i => i.PublishDate) : System.DateTime.MinValue;
    }

    public string AnimeTitle { get; }
    public IReadOnlyList<TorrentEntity> Items { get; }
    public int Count => Items.Count;
    public bool HasMatch { get; }
    public System.DateTime LatestDate { get; }
}
