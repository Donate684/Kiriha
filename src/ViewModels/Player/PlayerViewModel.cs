using Kiriha.Services.Data.Settings;
using System;
using System.Collections.Generic;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Mpv;
using Kiriha.Models;
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
    private readonly SettingsService? _settingsService;
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
    public List<PlayerMouseActionOption> MouseActionOptions { get; } = new()
    {
        new("Ничего", PlayerMouseAction.None),
        new("Пауза / воспроизведение", PlayerMouseAction.TogglePlayPause),
        new("Полноэкранный режим", PlayerMouseAction.ToggleFullscreen),
        new("Показать панель", PlayerMouseAction.ShowControls),
        new("Открыть настройки", PlayerMouseAction.OpenSettings),
        new("Назад на 10 секунд", PlayerMouseAction.SeekBackward10),
        new("Вперёд на 10 секунд", PlayerMouseAction.SeekForward10),
        new("Следующая аудиодорожка", PlayerMouseAction.CycleAudio),
        new("Следующие субтитры", PlayerMouseAction.CycleSubtitle)
    };
    public List<PlayerWheelActionOption> WheelActionOptions { get; } = new()
    {
        new("Ничего", PlayerWheelAction.None),
        new("Громче", PlayerWheelAction.VolumeUp),
        new("Тише", PlayerWheelAction.VolumeDown),
        new("Вперёд", PlayerWheelAction.SeekForward),
        new("Назад", PlayerWheelAction.SeekBackward),
        new("Скорость выше", PlayerWheelAction.SpeedUp),
        new("Скорость ниже", PlayerWheelAction.SpeedDown)
    };
    public List<int> WheelStepOptions { get; } = new() { 1, 2, 5, 10 };
    public List<int> SeekStepOptions { get; } = new() { 1, 3, 5, 10, 15, 30 };
    public List<string> ScreenshotFormatOptions { get; } = new() { "png", "jpg", "webp" };
    public List<ScreenshotResolutionOption> ScreenshotResolutionOptions { get; } = new()
    {
        new("Исходное видео", "video"),
        new("Размер окна", "window")
    };

    [ObservableProperty] private string _videoUrl = string.Empty;
    [ObservableProperty] private string _originalTitle = string.Empty;
    [ObservableProperty] private string _animeTitle = string.Empty;
    [ObservableProperty] private string _animeTitleRu = string.Empty;
    [ObservableProperty] private string _animeTitleEn = string.Empty;
    [ObservableProperty] private string _episodeTitle = string.Empty;
    [ObservableProperty] private string _rawEpisodeText = string.Empty;



    [ObservableProperty] private bool _playerAutoPlay = true;
    [ObservableProperty] private bool _singlePlayerWindow = true;
    [ObservableProperty] private bool _rememberPlayerVolume = true;
    [ObservableProperty] private bool _autoHideControls = true;
    [ObservableProperty] private bool _showChapterMarkers = true;

    [ObservableProperty] private bool _showPlayPauseButton = true;
    [ObservableProperty] private bool _showSkipButtons = true;
    [ObservableProperty] private bool _showMuteButton = true;
    [ObservableProperty] private bool _showVolumeSlider = true;
    [ObservableProperty] private bool _showTimeDisplay = true;
    [ObservableProperty] private bool _showSpeedButton = true;
    [ObservableProperty] private bool _showSubtitleButton = true;
    [ObservableProperty] private bool _showSubtitlePositionButton = true;
    [ObservableProperty] private bool _showAudioButton = true;
    [ObservableProperty] private bool _showScreenshotButton = true;
    [ObservableProperty] private bool _showSubtitleStyleButton = true;
    [ObservableProperty] private string _preferredAudioLanguages = "Japanese,jpn,ja";


    private int? _animeId;
    private bool _isInitializing;

    public PlayerViewModel(
        string videoUrl,
        PlayerMediaMetadata? metadata = null,
        IPlayerMediaMetadataResolver? metadataResolver = null,
        SettingsService? settingsService = null)
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

public record PlayerMouseActionOption(string Name, PlayerMouseAction Value);
public record PlayerWheelActionOption(string Name, PlayerWheelAction Value);
public record ScreenshotResolutionOption(string Name, string Value);
