using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Mpv.UI.ViewModels.Player.Settings;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    [ObservableProperty] private bool _playerAutoPlay = true;
    [ObservableProperty] private bool _singlePlayerWindow = true;
    [ObservableProperty] private bool _rememberPlaybackPosition = true;
    [ObservableProperty] private bool _rememberPlayerVolume = true;
    [ObservableProperty] private bool _autoHideControls = true;
    [ObservableProperty] private double _autoHideTimeout = 1.5;
    [ObservableProperty] private bool _showChapterMarkers = true;
    [ObservableProperty] private bool _smartTrackAutoload = true;

    [ObservableProperty] private SkipOpeningEndingMode _skipOpeningEnding = SkipOpeningEndingMode.Prompt;
    [ObservableProperty] private SkipOpeningEndingOption _selectedSkipOpeningEndingOption = null!;

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

    public List<SkipOpeningEndingOption> SkipOpeningEndingOptions { get; private set; } = null!;
    public List<PlayerMouseActionOption> MouseActionOptions { get; private set; } = null!;
    public List<PlayerWheelActionOption> WheelActionOptions { get; private set; } = null!;
    public List<int> WheelStepOptions { get; } = new() { 1, 2, 5, 10 };
    public List<int> SeekStepOptions { get; } = new() { 1, 3, 5, 10, 15, 30 };
    public List<string> ScreenshotFormatOptions { get; } = new() { "png", "jpg", "webp" };
    public List<ScreenshotResolutionOption> ScreenshotResolutionOptions { get; private set; } = null!;

    private void InitializeOptions()
    {
        SkipOpeningEndingOptions = SkipOpeningEndingOptionFactory.CreateDefaultOptions(key => _localizer.GetLoc(key));
        SelectedSkipOpeningEndingOption = SkipOpeningEndingOptions.FirstOrDefault(x => x.Value == SkipOpeningEnding) ?? SkipOpeningEndingOptions[1];

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

    partial void OnSelectedSkipOpeningEndingOptionChanged(SkipOpeningEndingOption value)
    {
        if (value != null && !_isApplyingSettings)
        {
            SkipOpeningEnding = value.Value;
        }
    }

    [ObservableProperty]
    private bool _skipOp = true;

    [ObservableProperty]
    private bool _skipEd = true;

    [ObservableProperty]
    private bool _skipMixedOp = true;

    [ObservableProperty]
    private bool _skipMixedEd = true;

    [ObservableProperty]
    private bool _skipRecap = true;

    partial void OnSkipOpChanged(bool value)
    {
        if (!_isApplyingSettings)
        {
            _settingsService?.Update(settings => settings.Player.SkipOp = value, SettingsSection.Player);
        }
    }

    partial void OnSkipEdChanged(bool value)
    {
        if (!_isApplyingSettings)
        {
            _settingsService?.Update(settings => settings.Player.SkipEd = value, SettingsSection.Player);
        }
    }

    partial void OnSkipMixedOpChanged(bool value)
    {
        if (!_isApplyingSettings)
        {
            _settingsService?.Update(settings => settings.Player.SkipMixedOp = value, SettingsSection.Player);
        }
    }

    partial void OnSkipMixedEdChanged(bool value)
    {
        if (!_isApplyingSettings)
        {
            _settingsService?.Update(settings => settings.Player.SkipMixedEd = value, SettingsSection.Player);
        }
    }

    partial void OnSkipRecapChanged(bool value)
    {
        if (!_isApplyingSettings)
        {
            _settingsService?.Update(settings => settings.Player.SkipRecap = value, SettingsSection.Player);
        }
    }

    partial void OnSkipOpeningEndingChanged(SkipOpeningEndingMode value)
    {
        if (!_isApplyingSettings)
        {
            _settingsService?.Update(settings => settings.Player.SkipOpeningEnding = value, SettingsSection.Player);
            if (value == SkipOpeningEndingMode.Disabled)
            {
                Overlay.DismissSkipPrompt(smooth: false);
            }
        }

        if (SkipOpeningEndingOptions != null)
        {
            var match = SkipOpeningEndingOptions.FirstOrDefault(x => x.Value == value);
            if (match != null && SelectedSkipOpeningEndingOption != match)
            {
                SelectedSkipOpeningEndingOption = match;
                OnPropertyChanged(nameof(SelectedSkipOpeningEndingOption));
            }
        }
    }

    public string ScreenshotFolderTitle { get; private set; } = string.Empty;

    public List<string> MpvScaleOptions { get; } = new() { "bilinear", "spline36", "lanczos", "ewa_lanczossharp", "mitchell", "oversample", "nearest" };
    public List<string> MpvDitherDepthOptions { get; } = new() { "auto", "no", "8", "10", "12", "16" };
    public List<string> MpvHwdecOptions { get; } = new() { "auto", "auto-safe", "auto-copy", "no", "d3d11va", "d3d11va-copy", "dxva2", "dxva2-copy", "vulkan", "vulkan-copy", "nvdec", "nvdec-copy" };
    public List<string> MpvVoOptions { get; } = new() { "gpu-next", "gpu" };
    public List<string> MpvGpuApiOptions { get; } = new() { "auto", "d3d11", "vulkan", "opengl" };
    public List<string> MpvGpuContextOptions { get; } = new() { "auto", "d3d11", "winvk", "win", "angle" };
}