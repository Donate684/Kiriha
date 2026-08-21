using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Domain.Models;
using Kiriha.Mpv.UI.ViewModels.Player.Settings;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    [ObservableProperty] private bool _playerAutoPlay = true;
    [ObservableProperty] private bool _singlePlayerWindow = true;
    [ObservableProperty] private bool _rememberPlayerVolume = true;
    [ObservableProperty] private bool _autoHideControls = true;
    [ObservableProperty] private double _autoHideTimeout = 1.5;
    [ObservableProperty] private bool _showChapterMarkers = true;
    [ObservableProperty] private bool _smartTrackAutoload = true;

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

    public List<PlayerMouseActionOption> MouseActionOptions { get; private set; } = null!;
    public List<PlayerWheelActionOption> WheelActionOptions { get; private set; } = null!;
    public List<int> WheelStepOptions { get; } = new() { 1, 2, 5, 10 };
    public List<int> SeekStepOptions { get; } = new() { 1, 3, 5, 10, 15, 30 };
    public List<string> ScreenshotFormatOptions { get; } = new() { "png", "jpg", "webp" };
    public List<ScreenshotResolutionOption> ScreenshotResolutionOptions { get; private set; } = null!;

    private void InitializeOptions()
    {
        MouseActionOptions = new()
        {
            new(_localizer.GetLoc("player.options.none"), PlayerMouseAction.None),
            new(_localizer.GetLoc("player.options.play_pause"), PlayerMouseAction.TogglePlayPause),
            new(_localizer.GetLoc("player.options.fullscreen"), PlayerMouseAction.ToggleFullscreen),
            new(_localizer.GetLoc("player.options.show_controls"), PlayerMouseAction.ShowControls),
            new(_localizer.GetLoc("player.options.open_settings"), PlayerMouseAction.OpenSettings),
            new(_localizer.GetLoc("player.options.seek_backward_10s"), PlayerMouseAction.SeekBackward10),
            new(_localizer.GetLoc("player.options.seek_forward_10s"), PlayerMouseAction.SeekForward10),
            new(_localizer.GetLoc("player.options.next_audio"), PlayerMouseAction.CycleAudio),
            new(_localizer.GetLoc("player.options.next_subtitle"), PlayerMouseAction.CycleSubtitle)
        };

        WheelActionOptions = new()
        {
            new(_localizer.GetLoc("player.options.none"), PlayerWheelAction.None),
            new(_localizer.GetLoc("player.options.volume_up"), PlayerWheelAction.VolumeUp),
            new(_localizer.GetLoc("player.options.volume_down"), PlayerWheelAction.VolumeDown),
            new(_localizer.GetLoc("player.options.seek_forward"), PlayerWheelAction.SeekForward),
            new(_localizer.GetLoc("player.options.seek_backward"), PlayerWheelAction.SeekBackward),
            new(_localizer.GetLoc("player.options.speed_up"), PlayerWheelAction.SpeedUp),
            new(_localizer.GetLoc("player.options.speed_down"), PlayerWheelAction.SpeedDown)
        };

        ScreenshotResolutionOptions = new()
        {
            new(_localizer.GetLoc("player.options.scale_video"), "video"),
            new(_localizer.GetLoc("player.options.scale_window"), "window")
        };

        PlaybackStatusMessage = _localizer.GetLoc("player.state.loading");
        ScreenshotFolderTitle = _localizer.GetLoc("player.settings_adv.folder");
    }

    public string ScreenshotFolderTitle { get; private set; } = string.Empty;

    public List<string> MpvScaleOptions { get; } = new() { "bilinear", "spline36", "lanczos", "ewa_lanczossharp", "mitchell", "oversample", "nearest" };
    public List<string> MpvDitherDepthOptions { get; } = new() { "auto", "no", "8", "10", "12", "16" };
    public List<string> MpvHwdecOptions { get; } = new() { "auto", "auto-safe", "auto-copy", "no", "d3d11va", "d3d11va-copy", "dxva2", "dxva2-copy", "vulkan", "vulkan-copy", "nvdec", "nvdec-copy" };
    public List<string> MpvVoOptions { get; } = new() { "gpu-next", "gpu" };
    public List<string> MpvGpuApiOptions { get; } = new() { "auto", "d3d11", "vulkan", "opengl" };
    public List<string> MpvGpuContextOptions { get; } = new() { "auto", "d3d11", "winvk", "win", "angle" };
}
