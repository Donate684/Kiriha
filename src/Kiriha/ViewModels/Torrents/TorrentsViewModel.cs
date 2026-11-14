using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Models.TorrServer;
using Kiriha.Core.Streaming.Abstractions;
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
    private readonly IStreamingService? _streamingService;
    private readonly IInternalPlayerServer? _internalPlayerServer;

    private const string PlaybackErrorLocKey = "torrents.streaming.playback_error";
    private const string EpisodesCountLocKey = "torrents.results.actions.episodes_count";

    public BulkObservableCollection<TorrentEntity> Torrents { get; } = new();

    public BulkObservableCollection<TorrentGroup> GroupedTorrents { get; } = new();

    public BulkObservableCollection<AnimeEntity> WatchingAnime { get; } = new();

    public ObservableCollection<HideableAnimeItem> HideMenuItems { get; } = new();

    public ObservableCollection<TorrServerFileItem> EpisodeFiles { get; } = new();

    public BulkObservableCollection<OnlineAnimeItem> OnlineResults { get; } = new();

    public ObservableCollection<OnlineEpisodeItem> OnlineEpisodes { get; } = new();

    public ObservableCollection<OnlineSourceItem> OnlineSources { get; } = new();

    public ObservableCollection<OnlineSourceItem> DisplayedOnlineSources { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewQuery))]
    [NotifyPropertyChangedFor(nameof(HasSearchQuery))]
    private string _searchQuery = string.Empty;

    public bool HasSearchQuery => !string.IsNullOrEmpty(SearchQuery);

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyResults))]
    [NotifyPropertyChangedFor(nameof(HasTorrentResults))]
    [NotifyPropertyChangedFor(nameof(HasOnlineResults))]
    private bool _isOnlineMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAnime))]
    private AnimeEntity? _selectedAnime;

    public bool HasSelectedAnime => SelectedAnime != null;

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
    private bool _isPlayerLaunching;

    [ObservableProperty]
    private string _playerLaunchingTitle = string.Empty;

    [ObservableProperty]
    private TorrentEntity? _activeStreamingTorrent;

    [ObservableProperty]
    private string _episodeSelectorCountText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActiveOnlineFhdBadge))]
    private OnlineAnimeItem? _activeOnlineAnime;

    [ObservableProperty]
    private OnlineEpisodeItem? _selectedOnlineEpisode;

    [ObservableProperty]
    private bool _isOnlineEpisodeSelectorVisible;

    [ObservableProperty]
    private bool _isOnlineEpisodeSelectorLoading;

    [ObservableProperty]
    private bool _isOnlineSourcesLoading;

    [ObservableProperty]
    private string? _onlineEpisodeSelectorError;

    [ObservableProperty]
    private string _onlineEpisodeSelectorCountText = string.Empty;

    public bool IsEmptyResults => IsOnlineMode ? OnlineResults.Count == 0 : GroupedTorrents.Count == 0;
    public bool HasTorrentResults => !IsOnlineMode && GroupedTorrents.Count > 0;
    public bool HasOnlineResults => IsOnlineMode && OnlineResults.Count > 0;

    private string? _currentStreamingHash;
    private string? _launchedStreamingHash;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _modalCts;
    private CancellationTokenSource? _launchCts;
    private readonly IProgressUpdateService? _progressService;

    public TorrentsViewModel(
        IRssFeedService rssService,
        IAnimeRepository animeRepo,
        ISettingsService settingsService,
        ITorrentFilterRepository torrentFilterRepo,
        ITorrServerService torrServer,
        ILocalizer localizer,
        IStreamingService? streamingService = null,
        IInternalPlayerServer? internalPlayerServer = null,
        IProgressUpdateService? progressService = null)
    {
        _rssService = rssService;
        _animeRepo = animeRepo;
        _settingsService = settingsService;
        _torrentFilterRepo = torrentFilterRepo;
        _torrServer = torrServer;
        _localizer = localizer;
        _streamingService = streamingService;
        _internalPlayerServer = internalPlayerServer;
        _progressService = progressService;

        LoadFilterSettings();
        LoadOnlineFilterSettings();

        Torrents.CollectionChanged += (_, _) =>
        {
            RebuildGroupedTorrents();
            OnPropertyChanged(nameof(IsEmptyResults));
            OnPropertyChanged(nameof(HasTorrentResults));
        };
        GroupedTorrents.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmptyResults));
            OnPropertyChanged(nameof(HasTorrentResults));
        };
        OnlineResults.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmptyResults));
            OnPropertyChanged(nameof(HasOnlineResults));
        };
        _animeRepo.Collection.CollectionChanged += (_, _) =>
        {
            RefreshWatchingList();
            if (IsOnlineEpisodeSelectorVisible && ActiveOnlineAnime != null)
            {
                var matched = ResolveMatchedAnime(ActiveOnlineAnime.Title);
                if (matched != null && !ReferenceEquals(_activeMatchedAnime, matched))
                {
                    SetActiveMatchedAnime(matched);
                    UpdateEpisodeWatchedStates(matched);
                }
            }
        };
        RefreshWatchingList();

        WeakReferenceMessenger.Default.Register<AnimeProgressChangedMessage>(this, (r, m) =>
        {
            Dispatcher.UIThread.Post(() => ((TorrentsViewModel)r).OnAnimeProgressChanged(m));
        });
        WeakReferenceMessenger.Default.Register<AnimeMatchedMessage>(this, (r, m) =>
        {
            Dispatcher.UIThread.Post(() => ((TorrentsViewModel)r).OnAnimeMatched(m));
        });
        WeakReferenceMessenger.Default.Register<AnimeListRefreshMessage>(this, (r, m) =>
        {
            Dispatcher.UIThread.Post(() => ((TorrentsViewModel)r).OnAnimeListRefreshed());
        });
    }
}
