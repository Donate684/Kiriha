using Kiriha.Services.Data.Settings;
using System;
using System.Collections.Generic;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Mpv;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Models.Presentation;
using Kiriha.Services;
using Kiriha.Services.Data;

namespace Kiriha.ViewModels.Player;

public partial class PlayerViewModel : ObservableObject, IDisposable
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".ts", ".m2ts", ".mpg", ".mpeg", ".ogm", ".ogg"
    };

    private readonly IPlayerMediaMetadataResolver? _metadataResolver;
    private readonly Kiriha.Core.Abstractions.Services.ISettingsService? _settingsService;
    private readonly PlayerPlaybackController _playback = new();
    private readonly PlayerStatePublisher _statePublisher;
    private readonly PlayerTimelineService _timeline = new();
    private readonly PlayerSettingsApplier _settingsApplier;
    private readonly PlayerTimelinePreviewController _timelinePreview;
    private DispatcherTimer? _timer;
    private bool _isApplyingSettings;
    private bool _mpvRuntimeDiagnosticsVisible;

    public PlayerOverlayViewModel Overlay { get; } = new();

    public System.Collections.ObjectModel.ObservableCollection<TrackInfo> AudioTracks { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<TrackInfo> SubtitleTracks { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<ChapterInfo> Chapters { get; } = new();

    [ObservableProperty] private string _videoUrl = string.Empty;
    [ObservableProperty] private string _originalTitle = string.Empty;
    [ObservableProperty] private string _animeTitle = string.Empty;
    [ObservableProperty] private string _animeTitleRu = string.Empty;
    [ObservableProperty] private string _animeTitleEn = string.Empty;
    [ObservableProperty] private string _episodeTitle = string.Empty;
    [ObservableProperty] private string _rawEpisodeText = string.Empty;


    private int? _animeId;
    private bool _isInitializing;

    public PlayerViewModel(
        string videoUrl,
        PlayerMediaMetadata? metadata = null,
        IPlayerMediaMetadataResolver? metadataResolver = null,
        Kiriha.Core.Abstractions.Services.ISettingsService? settingsService = null)
    {
        _isInitializing = true;
        _metadataResolver = metadataResolver;
        _settingsService = settingsService;
        _statePublisher = new PlayerStatePublisher(CreatePlayerState);
        _settingsApplier = new PlayerSettingsApplier(_playback);
        _timelinePreview = new PlayerTimelinePreviewController(Overlay);
        ApplyPlayerSettings();
        ApplyMetadata(metadata ?? metadataResolver?.Resolve(videoUrl) ?? PlayerMediaMetadata.FromVideoPath(videoUrl));

        VideoUrl = videoUrl; // Sets VideoUrl and triggers OnVideoUrlChanged if needed, but since it's constructor, we already set the fields above.
        _isInitializing = false;
    }



}
