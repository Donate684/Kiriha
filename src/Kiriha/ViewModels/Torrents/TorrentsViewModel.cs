using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Models.TorrServer;
using Kiriha.Utils.Collections;

namespace Kiriha.ViewModels.Torrents;

public partial class TorrentsViewModel : ViewModelBase
{
    private readonly IRssFeedService _rssService;
    private readonly IAnimeRepository _animeRepo;
    private readonly ISettingsService _settingsService;
    private readonly ITorrentFilterRepository _torrentFilterRepo;
    private readonly ITorrServerService _torrServer;
    private readonly ILocalizer _localizer;

    public BulkObservableCollection<TorrentEntity> Torrents { get; } = new();

    public BulkObservableCollection<TorrentGroup> GroupedTorrents { get; } = new();

    public BulkObservableCollection<AnimeEntity> WatchingAnime { get; } = new();

    public ObservableCollection<HideableAnimeItem> HideMenuItems { get; } = new();

    public ObservableCollection<TorrServerFileItem> EpisodeFiles { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private AnimeEntity? _selectedAnime;

    [ObservableProperty]
    private bool _isHideMode;

    [ObservableProperty]
    private bool _isStreamingLoading;

    [ObservableProperty]
    private string _streamingStatusText = string.Empty;

    [ObservableProperty]
    private string? _streamingErrorMessage;

    [ObservableProperty]
    private bool _isEpisodeSelectorVisible;

    [ObservableProperty]
    private bool _isEpisodeSelectorLoading;

    [ObservableProperty]
    private string? _episodeSelectorError;

    [ObservableProperty]
    private TorrentEntity? _activeStreamingTorrent;

    [ObservableProperty]
    private string _episodeSelectorCountText = string.Empty;

    private string? _currentStreamingHash;
    private CancellationTokenSource? _streamingCts;

    public TorrentsViewModel(
        IRssFeedService rssService,
        IAnimeRepository animeRepo,
        ISettingsService settingsService,
        ITorrentFilterRepository torrentFilterRepo,
        ITorrServerService torrServer,
        ILocalizer localizer)
    {
        _rssService = rssService;
        _animeRepo = animeRepo;
        _settingsService = settingsService;
        _torrentFilterRepo = torrentFilterRepo;
        _torrServer = torrServer;
        _localizer = localizer;

        LoadFilterSettings();

        Torrents.CollectionChanged += (_, _) => RebuildGroupedTorrents();
        _animeRepo.Collection.CollectionChanged += (_, _) => RefreshWatchingList();
        RefreshWatchingList();
    }
}
