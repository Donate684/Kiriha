using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Messages;
using Kiriha.Core.Models;
using Kiriha.Core.Player;
using Kiriha.Core.Repositories;
using Kiriha.Core.Services;
using Kiriha.Core.Shiki;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Integration;

using Kiriha.Core.Abstractions.Models;
using Kiriha.Core.Abstractions.Models.Api;
using Kiriha.Core.Abstractions.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Core;

public partial class TrackingService : IDisposable
{
    private readonly AnisthesiaService _anisthesiaService;
    private readonly IMappingService _mappingService;
    private readonly IAnimeRepository _animeRepo;
    private readonly ISettingsService _settingsService;
    private readonly IDiscordService _discordService;
    private readonly IScrobbleService _scrobbleService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IEnumerable<ITrackerService> _trackers;
    private readonly MediaMatchingPipeline _pipeline;

    // _state guards _currentMedia and _matchedAnime which are read/written from the
    // Anisthesia background thread (MediaDetected/MediaCleared) and from UI command handlers.
    private readonly object _state = new();
    private ParsedMedia? _currentMedia;
    private AnimeEntity? _matchedAnime;
    private bool _manualMapInProgress;

    public ParsedMedia? CurrentMedia { get { lock (_state) return _currentMedia; } }
    public AnimeEntity? MatchedAnime { get { lock (_state) return _matchedAnime; } }

    public TrackingService(
        AnisthesiaService anisthesiaService,
        IMappingService mappingService,
        IAnimeRepository animeRepo,
        Kiriha.Core.Services.ISettingsService settingsService,
        IDiscordService discordService,
        IScrobbleService scrobbleService,
        IUiDispatcher uiDispatcher,
        IEnumerable<ITrackerService> trackers,
        MediaMatchingPipeline pipeline)
    {
        _anisthesiaService = anisthesiaService;
        _mappingService = mappingService;
        _animeRepo = animeRepo;
        _settingsService = settingsService;
        _discordService = discordService;
        _scrobbleService = scrobbleService;
        _uiDispatcher = uiDispatcher;
        _trackers = trackers;
        _pipeline = pipeline;

        _anisthesiaService.MediaDetected += OnMediaDetected;
        _anisthesiaService.MediaCleared += OnMediaCleared;
        _scrobbleService.CountdownUpdated += OnScrobbleCountdownUpdated;
    }

    public void Dispose()
    {
        _anisthesiaService.MediaDetected -= OnMediaDetected;
        _anisthesiaService.MediaCleared -= OnMediaCleared;
        _scrobbleService.CountdownUpdated -= OnScrobbleCountdownUpdated;
        _scrobbleService.CancelScrobble();
    }
}
