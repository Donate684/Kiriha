using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Models.Presentation;

namespace Kiriha.ViewModels.Player;

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
    public List<string> MpvScaleOptions { get; } = new() { "bilinear", "spline36", "lanczos", "ewa_lanczossharp", "mitchell", "oversample", "nearest" };
    public List<string> MpvDitherDepthOptions { get; } = new() { "auto", "no", "8", "10", "12", "16" };
    public List<string> MpvHwdecOptions { get; } = new() { "auto", "auto-safe", "auto-copy", "no", "d3d11va", "d3d11va-copy", "dxva2", "dxva2-copy", "vulkan", "vulkan-copy", "nvdec", "nvdec-copy" };
    public List<string> MpvVoOptions { get; } = new() { "gpu-next", "gpu" };
    public List<string> MpvGpuApiOptions { get; } = new() { "auto", "d3d11", "vulkan", "opengl" };
    public List<string> MpvGpuContextOptions { get; } = new() { "auto", "d3d11", "winvk", "win", "angle" };
}
