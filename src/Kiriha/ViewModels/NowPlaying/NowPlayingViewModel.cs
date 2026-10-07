using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Services.Data.Metadata;
using Serilog;

namespace Kiriha.ViewModels.NowPlaying;

public partial class NowPlayingViewModel : ViewModelBase, IDisposable,
    IRecipient<MediaChangedMessage>,
    IRecipient<AnimeMatchedMessage>,
    IRecipient<TrackingCountdownMessage>,
    IRecipient<TrackingStatusMessage>
{
    private readonly ITrackingService _trackingService;
    // Tracks the anime id of an in-flight manual selection. Until the background
    // TrackingService fires AnimeMatched with this id (or null on a media change),
    // we ignore intermediate null/other matches so they don't clobber the UI choice.
    // 0 means "no manual selection pending".
    private int _pendingManualMatchId;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizer _localizer;
    private readonly IAnimeRepository _animeRepo;
    private readonly IAnimeListActionService _listActionService;
    private readonly IShikiMetadataService _shikiMetadataService;
    private readonly IMalApiService _malApi;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayEpisodeNumber))]
    private ParsedMedia? _currentMedia;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotInList))]
    [NotifyPropertyChangedFor(nameof(AllAlternativeTitles))]
    [NotifyPropertyChangedFor(nameof(HasAlternativeTitles))]
    [NotifyPropertyChangedFor(nameof(DisplayEpisodeNumber))]
    private AnimeEntity? _matchedAnime;
    
    [ObservableProperty] private AnimeEntity? _pendingMatch;

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isSearching;

    [ObservableProperty] private bool _isManuallyMapped;
    [ObservableProperty] private bool _isLoadingDetails;

    public bool IsNotInList => MatchedAnime != null && MatchedAnime.Status == UserAnimeStatus.None;

    public string DisplayEpisodeNumber =>
        !string.IsNullOrWhiteSpace(CurrentMedia?.Episode)
            ? CurrentMedia.Episode
            : (MatchedAnime?.TotalEpisodes == 1 ? "1" : "?");

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

    public string DisplayStatus => !IsMediaDetected ? _localizer.GetLoc("scrobbler.status.ready") :
                                   (!string.IsNullOrEmpty(TrackingStatus) ? TrackingStatus :
                                   (IsPaused ? _localizer.GetLoc("scrobbler.status.paused") :
                                   (string.IsNullOrEmpty(CountdownStatus) ? _localizer.GetLoc("scrobbler.status.active") : CountdownStatus)));

    public ISettingsService Settings => _settingsService;
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
        ITrackingService trackingService,
        ISettingsService settingsService,
        IAnimeRepository animeRepo,
        IAnimeListActionService listActionService,
        IShikiMetadataService shikiMetadataService,
        IMalApiService malApi,
        ILocalizer localizer)
    {
        _trackingService = trackingService;
        _localizer = localizer;
        _settingsService = settingsService;
        _animeRepo = animeRepo;
        _listActionService = listActionService;
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






