using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Integration;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Services.Data.Repository;
using Kiriha.Services.Data.Settings;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Services.Data;
using Kiriha.Core.Tracking;

namespace Kiriha.ViewModels.Torrents;

public partial class TorrentsViewModel : ViewModelBase
{
    private readonly RssFeedService _rssService;
    private readonly Kiriha.Core.Abstractions.Repositories.IAnimeRepository _animeRepo;
    private readonly Kiriha.Core.Abstractions.Services.ISettingsService _settingsService;

    public ObservableCollection<TorrentEntity> Torrents { get; } = new();

    public ObservableCollection<TorrentGroup> GroupedTorrents { get; } = new();

    public ObservableCollection<AnimeEntity> WatchingAnime { get; } = new();

    public ObservableCollection<HideableAnimeItem> HideMenuItems { get; } = new();

    public static TorrentSortMode[] AvailableSortModes { get; } =
        new[] { TorrentSortMode.Newest, TorrentSortMode.Matched, TorrentSortMode.ReleaseGroup };

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private AnimeEntity? _selectedAnime;

    [ObservableProperty]
    private bool _isHideMode;

    public TorrentsViewModel(RssFeedService rssService, Kiriha.Core.Abstractions.Repositories.IAnimeRepository animeRepo, Kiriha.Core.Abstractions.Services.ISettingsService settingsService)
    {
        _rssService = rssService;
        _animeRepo = animeRepo;
        _settingsService = settingsService;

        LoadFilterSettings();

        Torrents.CollectionChanged += (_, _) => RebuildGroupedTorrents();
        RefreshWatchingList();
    }
}
