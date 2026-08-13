using Kiriha.Core.Models;
using Kiriha.ViewModels.Main;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Dialogs;
using Kiriha.ViewModels.Startup;
using Kiriha.ViewModels.Settings;
using Kiriha.Core.Tracking.Integration;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Core.Repositories;
using Kiriha.Services.Data.Repository;
using Kiriha.Services.Data.Metadata;
using Kiriha.Services.Data.Mapping;
using Kiriha.Services.Data.Settings;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Core.Platform;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Kiriha.Core.Abstractions.Models.Entities;
using Kiriha.Core.Messages;
using Kiriha.Services.Data;
using Kiriha.Utils.Async;
using Serilog;

namespace Kiriha.ViewModels.NowPlaying;

public partial class NowPlayingViewModel : ViewModelBase, IDisposable,
    IRecipient<MediaChangedMessage>,
    IRecipient<AnimeMatchedMessage>,
    IRecipient<TrackingCountdownMessage>,
    IRecipient<TrackingStatusMessage>
{
    private readonly Kiriha.Core.Tracking.Core.TrackingService _trackingService;
    // Tracks the anime id of an in-flight manual selection. Until the background
    // TrackingService fires AnimeMatched with this id (or null on a media change),
    // we ignore intermediate null/other matches so they don't clobber the UI choice.
    // 0 means "no manual selection pending".
    private int _pendingManualMatchId;
    private readonly Kiriha.Core.Services.ISettingsService _settingsService;
    private readonly MappingService _mappingService;
    private readonly Kiriha.Core.Repositories.IAnimeRepository _animeRepo;
    private readonly Kiriha.Core.Services.IProgressUpdateService _progressService;
    private readonly Kiriha.Core.Services.ISyncManager _syncManager;
    private readonly ShikiMetadataService _shikiMetadataService;
    private readonly Kiriha.Core.Services.IMalApiService _malApi;

    [ObservableProperty] private ParsedMedia? _currentMedia;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotInList))]
    [NotifyPropertyChangedFor(nameof(AllAlternativeTitles))]
    [NotifyPropertyChangedFor(nameof(HasAlternativeTitles))]
    private AnimeEntity? _matchedAnime;
    
    [ObservableProperty] private AnimeEntity? _pendingMatch;

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isSearching;

    [ObservableProperty] private bool _isManuallyMapped;

    public bool IsNotInList => MatchedAnime != null && MatchedAnime.Status == UserAnimeStatus.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private bool _isMediaDetected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private string _countdownStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private string _trackingStatus = string.Empty;

    public string DisplayStatus => !IsMediaDetected ? UIUtils.GetLoc("scrobbler.status.ready") :
                                   (!string.IsNullOrEmpty(TrackingStatus) ? TrackingStatus :
                                   (IsPaused ? UIUtils.GetLoc("scrobbler.status.paused") :
                                   (string.IsNullOrEmpty(CountdownStatus) ? UIUtils.GetLoc("scrobbler.status.active") : CountdownStatus)));

    public Kiriha.Core.Services.ISettingsService Settings => _settingsService;
    public bool IsScrobblerEnabled => _settingsService.Current.System.Scrobbler.Enabled;

    public ObservableCollection<string> DetectionLogs { get; } = new();
    public ObservableCollection<AnimeEntity> Suggestions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestions))]
    private bool _showSuggestions;

    public bool HasSuggestions => ShowSuggestions && Suggestions.Count > 0;

    [ObservableProperty] private bool _isSearchPanelOpen;

    private CancellationTokenSource? _searchCts;
    private readonly CancellationTokenSource _disposeCts = new();

    public NowPlayingViewModel(
        Kiriha.Core.Tracking.Core.TrackingService trackingService,
        Kiriha.Core.Services.ISettingsService settingsService,
        MappingService mappingService,
        Kiriha.Core.Repositories.IAnimeRepository animeRepo,
        Kiriha.Core.Services.IProgressUpdateService progressService,
        Kiriha.Core.Services.ISyncManager syncManager,
        ShikiMetadataService shikiMetadataService,
        Kiriha.Core.Services.IMalApiService malApi)
    {
        _trackingService = trackingService;
        _settingsService = settingsService;
        _mappingService = mappingService;
        _animeRepo = animeRepo;
        _progressService = progressService;
        _syncManager = syncManager;
        _shikiMetadataService = shikiMetadataService;
        _malApi = malApi;

        WeakReferenceMessenger.Default.RegisterAll(this);

        // Sync initial state if any
        CurrentMedia = _trackingService.CurrentMedia;
        MatchedAnime = _trackingService.MatchedAnime;
        IsMediaDetected = CurrentMedia != null;
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);

        try { _searchCts?.Cancel(); } catch (Exception ex) { Log.Debug(ex, "Error canceling search CTS during dispose"); }
        try { _searchCts?.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Error disposing search CTS"); }
        try { _disposeCts.Cancel(); } catch (Exception ex) { Log.Debug(ex, "Error canceling dispose CTS"); }
        try { _disposeCts.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Error disposing dispose CTS"); }
    }
}
