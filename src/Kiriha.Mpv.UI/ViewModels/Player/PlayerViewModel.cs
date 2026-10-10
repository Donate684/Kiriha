using System.Collections.Frozen;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Mpv.UI.Services.Player;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel : ObservableObject, IDisposable
{
    private static readonly FrozenSet<string> MediaExtensions = FrozenSet.ToFrozenSet(
        [
            ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".ts", ".m2ts", ".mpg", ".mpeg", ".ogm", ".ogg"
        ], StringComparer.OrdinalIgnoreCase);

    private readonly IPlayerMediaMetadataResolver? _metadataResolver;
    private readonly ISettingsService? _settingsService;
    private readonly ILocalizer _localizer;
    private readonly PlayerPlaybackController _playback = new();
    private readonly PlayerStatePublisher _statePublisher;
    private readonly PlayerTimelineService _timeline = new();
    private readonly PlayerSettingsApplier _settingsApplier;
    private readonly PlayerTimelinePreviewController _timelinePreview;
    private DispatcherTimer? _timer;
    private bool _isApplyingSettings;
    private bool _mpvRuntimeDiagnosticsVisible;
    private double _subtitleDelay;

    public PlayerOverlayViewModel Overlay { get; } = new();

    public System.Collections.ObjectModel.ObservableCollection<TrackInfo> AudioTracks { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<TrackInfo> SubtitleTracks { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<ChapterInfo> Chapters { get; } = new();

    [ObservableProperty] private string _videoUrl = string.Empty;
    [ObservableProperty] private string _originalTitle = string.Empty;
    [ObservableProperty] private string _animeTitle = string.Empty;
    [ObservableProperty] private string _animeTitleRu = string.Empty;
    [ObservableProperty] private string _animeTitleEn = string.Empty;
    [ObservableProperty] private string _animeTitleRomaji = string.Empty;
    [ObservableProperty] private string _episodeTitle = string.Empty;
    [ObservableProperty] private string _rawEpisodeText = string.Empty;


    private int? _animeId;
    public int? AnimeId => _animeId;
    private bool _isInitializing;
    private string? _previousVideoUrlForMetadata;

    private readonly ITorrServerService? _torrServer;

    public PlayerViewModel(
        string videoUrl,
        PlayerMediaMetadata? metadata,
        IPlayerMediaMetadataResolver? metadataResolver,
        ISettingsService? settingsService,
        ILocalizer localizer,
        ITorrServerService? torrServer = null)
    {
        _isInitializing = true;
        _metadataResolver = metadataResolver;
        _settingsService = settingsService;
        _localizer = localizer;
        _torrServer = torrServer;
        InitializeOptions();
        _statePublisher = new PlayerStatePublisher(CreatePlayerState);
        _statePublisher.MetadataReceived += OnExternalMetadataReceived;
        _settingsApplier = new PlayerSettingsApplier(_playback);
        _timelinePreview = new PlayerTimelinePreviewController(Overlay);
        _playback.BufferingChanged += OnPlayerBufferingChanged;
        ApplyPlayerSettings();
        var initialMetadata = metadata ?? metadataResolver?.Resolve(videoUrl) ?? PlayerMediaMetadata.FromVideoPath(videoUrl);
        ApplyMetadata(initialMetadata);

        _previousVideoUrlForMetadata = videoUrl;
        VideoUrl = videoUrl; // Sets VideoUrl and triggers OnVideoUrlChanged if needed, but since it's constructor, we already set the fields above.
        InitializeTorrentStream(videoUrl, initialMetadata);
        _isInitializing = false;
    }

    private void OnExternalMetadataReceived(PlayerMediaMetadata metadata)
    {
        if (MatchesTorrentHash(metadata.TorrentHash) || MatchesOriginalTitle(metadata.OriginalTitle))
        {
            Dispatcher.UIThread.Post(() => ApplyExternalMetadata(metadata));
        }
    }



}
