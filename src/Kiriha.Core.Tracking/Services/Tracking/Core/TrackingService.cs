using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Microsoft.Extensions.Hosting;

namespace Kiriha.Core.Tracking.Core;

public partial class TrackingService : ITrackingService, IHostedService
{
    private readonly IExternalMediaDetector _externalMediaDetector;
    private readonly IInternalPlayerServer _internalPlayerServer;
    private readonly IMappingService _mappingService;
    private readonly IAnimeRepository _animeRepo;
    private readonly ISettingsService _settingsService;
    private readonly IDiscordService _discordService;
    private readonly IScrobbleService _scrobbleService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IEnumerable<ITrackerService> _trackers;
    private readonly MediaMatchingPipeline _pipeline;
    private readonly IShikiMetadataService? _shikiMetadataService;
    private readonly ICoreEventBus _eventBus;
    private readonly IDisposable _refreshSubscription;

    // _state guards _currentMedia and _matchedAnime which are read/written from the
    // External media detector background thread (MediaDetected/MediaCleared) and from UI command handlers.
    private readonly Lock _state = new();
    private ParsedMedia? _currentMedia;
    private AnimeEntity? _matchedAnime;
    private bool _manualMapInProgress;
    private long _mediaVersion;

    public ParsedMedia? CurrentMedia { get { lock (_state) return _currentMedia; } }
    public AnimeEntity? MatchedAnime { get { lock (_state) return _matchedAnime; } }

    public TrackingService(
        IExternalMediaDetector externalMediaDetector, IInternalPlayerServer internalPlayerServer,
        IMappingService mappingService,
        IAnimeRepository animeRepo,
        ISettingsService settingsService,
        IDiscordService discordService,
        IScrobbleService scrobbleService,
        IUiDispatcher uiDispatcher,
        IEnumerable<ITrackerService> trackers,
        MediaMatchingPipeline pipeline,
        IShikiMetadataService? shikiMetadataService = null,
        ICoreEventBus? eventBus = null)
    {
        _externalMediaDetector = externalMediaDetector;
        _internalPlayerServer = internalPlayerServer;
        _internalPlayerServer.PlayerStateChanged += OnPlayerStateChanged;
        _mappingService = mappingService;
        _animeRepo = animeRepo;
        _settingsService = settingsService;
        _discordService = discordService;
        _scrobbleService = scrobbleService;
        _uiDispatcher = uiDispatcher;
        _trackers = trackers;
        _pipeline = pipeline;
        _shikiMetadataService = shikiMetadataService;
        _eventBus = eventBus ?? CoreEventBus.Default;

        _externalMediaDetector.MediaDetected += OnMediaDetected;
        _externalMediaDetector.MediaCleared += OnMediaCleared;
        _scrobbleService.CountdownUpdated += OnScrobbleCountdownUpdated;
        _refreshSubscription = _eventBus.Subscribe<AnimeListRefreshMessage>(_ => OnAnimeListRefreshed());
    }

    private void OnAnimeListRefreshed()
    {
        ParsedMedia? media;
        AnimeEntity? matched;
        long version;
        lock (_state)
        {
            media = _currentMedia;
            matched = _matchedAnime;
            version = _mediaVersion;
        }

        if (media != null && matched != null)
        {
            var inRepo = _animeRepo.FindById(matched.Id, MediaKind.Anime)
                ?? (_uiDispatcher.CheckAccess() ? _animeRepo.Collection?.FirstOrDefault(x => x.Id == matched.Id && x.MediaKind == MediaKind.Anime) : null);
            if (inRepo != null && inRepo.Status != UserAnimeStatus.None &&
                (!ReferenceEquals(matched, inRepo) || matched.Status == UserAnimeStatus.None))
            {
                lock (_state)
                {
                    if (!IsCurrentMediaVersion(version) || !ReferenceEquals(_matchedAnime, matched))
                        return;

                    _matchedAnime = inRepo;
                    var effectiveMedia = GetEffectiveMedia(_currentMedia ?? media, inRepo);
                    NotifyPlayerMetadata(effectiveMedia, inRepo);
                    UpdateDiscordPresence(effectiveMedia, inRepo);
                    _scrobbleService.StartScrobble(effectiveMedia, inRepo);
                }
            }
        }
    }

    private void OnPlayerStateChanged(object? sender, InternalPlayerState e) => SetInternalMedia(e);

    private int _disposed;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_internalPlayerServer.LatestState is { } existingState && !existingState.IsClosed)
        {
            SetInternalMedia(existingState);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _internalPlayerServer.PlayerStateChanged -= OnPlayerStateChanged;
        _externalMediaDetector.MediaDetected -= OnMediaDetected;
        _externalMediaDetector.MediaCleared -= OnMediaCleared;
        _scrobbleService.CountdownUpdated -= OnScrobbleCountdownUpdated;
        lock (_state)
            _scrobbleService.CancelScrobble();
        _refreshSubscription.Dispose();
    }
}

