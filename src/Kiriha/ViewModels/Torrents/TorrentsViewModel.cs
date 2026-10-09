using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Utils.Collections;

namespace Kiriha.ViewModels.Torrents;

public partial class TorrentsViewModel : ViewModelBase
{
    private readonly IRssFeedService _rssService;
    private readonly IAnimeRepository _animeRepo;
    private readonly ISettingsService _settingsService;
    private readonly ITorrentFilterRepository _torrentFilterRepo;

    public BulkObservableCollection<TorrentEntity> Torrents { get; } = new();

    public BulkObservableCollection<TorrentGroup> GroupedTorrents { get; } = new();

    public BulkObservableCollection<AnimeEntity> WatchingAnime { get; } = new();

    public ObservableCollection<HideableAnimeItem> HideMenuItems { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private AnimeEntity? _selectedAnime;

    [ObservableProperty]
    private bool _isHideMode;

    public TorrentsViewModel(
        IRssFeedService rssService,
        IAnimeRepository animeRepo,
        ISettingsService settingsService,
        ITorrentFilterRepository torrentFilterRepo)
    {
        _rssService = rssService;
        _animeRepo = animeRepo;
        _settingsService = settingsService;
        _torrentFilterRepo = torrentFilterRepo;

        LoadFilterSettings();

        Torrents.CollectionChanged += (_, _) => RebuildGroupedTorrents();
        _animeRepo.Collection.CollectionChanged += (_, _) => RefreshWatchingList();
        RefreshWatchingList();
    }
}



