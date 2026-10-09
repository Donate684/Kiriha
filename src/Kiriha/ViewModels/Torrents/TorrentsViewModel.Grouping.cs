using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.ViewModels.Torrents;

public partial class TorrentsViewModel
{
    public static readonly IReadOnlyList<TorrentSortOption> AvailableSortModes =
    [
        new(TorrentSortMode.Newest, "torrents.sort.newest"),
        new(TorrentSortMode.Matched, "torrents.sort.matched"),
        new(TorrentSortMode.ReleaseGroup, "torrents.sort.releasegroup")
    ];

    public IReadOnlyList<TorrentSortOption> SortModes => AvailableSortModes;

    [ObservableProperty]
    private TorrentSortOption _selectedSortOption = AvailableSortModes[0];

    partial void OnSelectedSortOptionChanged(TorrentSortOption value)
    {
        if (value is not null && SortMode != value.Mode)
        {
            SortMode = value.Mode;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private TorrentSortMode _sortMode = TorrentSortMode.Newest;

    partial void OnSortModeChanged(TorrentSortMode value)
    {
        var matched = AvailableSortModes.FirstOrDefault(x => x.Mode == value);
        if (matched != null && _selectedSortOption != matched)
        {
            _selectedSortOption = matched;
            OnPropertyChanged(nameof(SelectedSortOption));
        }
        RebuildGroupedTorrents();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterOnlyNew;

    partial void OnFilterOnlyNewChanged(bool value) => RebuildGroupedTorrents();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterOnlyBatches;

    partial void OnFilterOnlyBatchesChanged(bool value) => RebuildGroupedTorrents();

    private void RebuildGroupedTorrents()
    {
        GroupedTorrents.Reset(TorrentGrouping.Build(Torrents, SortMode, FilterOnlyNew, FilterOnlyBatches));
    }
}

internal static class TorrentGrouping
{
    public static IEnumerable<TorrentGroup> Build(
        IEnumerable<TorrentEntity> torrents,
        TorrentSortMode sortMode,
        bool filterOnlyNew = false,
        bool filterOnlyBatches = false)
    {
        var filtered = torrents;
        if (filterOnlyNew)
        {
            filtered = filtered.Where(t => t.IsMatched);
        }
        if (filterOnlyBatches)
        {
            filtered = filtered.Where(t => t.IsBatch);
        }

        var source = sortMode switch
        {
            TorrentSortMode.Matched =>
                filtered.OrderByDescending(t => t.IsMatched)
                    .ThenByDescending(t => t.PublishDate),
            TorrentSortMode.ReleaseGroup =>
                filtered.OrderBy(t => string.IsNullOrEmpty(t.ReleaseGroup) ? "zzz" : t.ReleaseGroup,
                        System.StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(t => t.PublishDate),
            _ => filtered.OrderByDescending(t => t.PublishDate),
        };

        return source
            .GroupBy(t => string.IsNullOrWhiteSpace(t.AnimeTitle) ? "\u2014" : t.AnimeTitle!.Trim(),
                System.StringComparer.OrdinalIgnoreCase)
            .Select(g => new TorrentGroup(g.Key, g.ToList()))
            .OrderByDescending(g => g.HasMatch)
            .ThenByDescending(g => g.LatestDate);
    }
}
