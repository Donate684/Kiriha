using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Mpv.UI.ViewModels.Player.Settings;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    [ObservableProperty] private string _screenshotDirectory = string.Empty;
    [ObservableProperty] private string _screenshotFormat = "png";
    [ObservableProperty] private ScreenshotResolutionOption? _screenshotResolution;
    [ObservableProperty] private int _screenshotPngCompression = 4;
    [ObservableProperty] private int _screenshotQuality = 95;
    [ObservableProperty] private bool _screenshotHighBitDepth = false;
}
