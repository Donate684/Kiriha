using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Models.Presentation;
using Kiriha.Mpv;

namespace Kiriha.ViewModels.Player;

public partial class PlayerViewModel
{
    [ObservableProperty] private string _mpvScale = "bilinear";
    [ObservableProperty] private string _mpvChromaScale = "bilinear";
    [ObservableProperty] private string _mpvDitherDepth = "auto";
    [ObservableProperty] private bool _mpvCorrectDownscaling = false;
    [ObservableProperty] private bool _mpvDeband = false;
    [ObservableProperty] private int _mpvDebandIterations = 1;
    [ObservableProperty] private int _mpvDebandThreshold = 30;
    [ObservableProperty] private string _mpvVideoPreset = "default";
    [ObservableProperty] private string _mpvHwdec = "auto";
    [ObservableProperty] private string _mpvVideoOutput = "gpu-next";
    [ObservableProperty] private string _mpvGpuApi = "auto";
    [ObservableProperty] private string _mpvGpuContext = "auto";
    [ObservableProperty] private bool _mpvVideoSync = false;
    [ObservableProperty] private bool _mpvInterpolation = false;
    [ObservableProperty] private MpvRuntimeDiagnostics _mpvRuntimeInfo = new();
    [ObservableProperty] private int _wheelVolumeStep = 5;
    [ObservableProperty] private int _seekStep = 1;
    [ObservableProperty] private PlayerMouseActionOption? _leftClickAction;
    [ObservableProperty] private PlayerMouseActionOption? _rightClickAction;
    [ObservableProperty] private PlayerMouseActionOption? _middleClickAction;
    [ObservableProperty] private PlayerWheelActionOption? _wheelUpAction;
    [ObservableProperty] private PlayerWheelActionOption? _wheelDownAction;
}
