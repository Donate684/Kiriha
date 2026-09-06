using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Infrastructure.Tracking.Integration;
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
using Kiriha.Core.Abstractions.Services;

namespace Kiriha.ViewModels.Torrents;

public partial class TorrentsViewModel : ViewModelBase
{
    private readonly RssFeedService _rssService;
    private readonly IAnimeRepository _animeRepo;
    private readonly ISettingsService _settingsService;

    public ObservableCollection<TorrentEntity> Torrents { get; } = new();

    public ObservableCollection<TorrentGroup> GroupedTorrents { get; } = new();

    public ObservableCollection<AnimeEntity> WatchingAnime { get; } = new();

    public ObservableCollection<HideableAnimeItem> HideMenuItems { get; } = new();

    public static TorrentSortMode[] AvailableSortModes { get; } =
        [TorrentSortMode.Newest, TorrentSortMode.Matched, TorrentSortMode.ReleaseGroup];

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private AnimeEntity? _selectedAnime;

    [ObservableProperty]
    private bool _isHideMode;

    public TorrentsViewModel(RssFeedService rssService, IAnimeRepository animeRepo, ISettingsService settingsService)
    {
        _rssService = rssService;
        _animeRepo = animeRepo;
        _settingsService = settingsService;

        LoadFilterSettings();

        Torrents.CollectionChanged += (_, _) => RebuildGroupedTorrents();
        RefreshWatchingList();
    }
}



