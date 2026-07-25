using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.ViewModels.Player;

public partial class PlayerViewModel
{
    [ObservableProperty] private string _mpvScale = "ewa_lanczossharp";
    [ObservableProperty] private string _mpvChromaScale = "ewa_lanczossharp";
    [ObservableProperty] private string _mpvDitherDepth = "auto";
    [ObservableProperty] private bool _mpvCorrectDownscaling = true;
    [ObservableProperty] private bool _mpvDeband = true;
    [ObservableProperty] private int _mpvDebandIterations = 3;
    [ObservableProperty] private int _mpvDebandThreshold = 30;
    [ObservableProperty] private string _mpvHwdec = "auto";
    [ObservableProperty] private string _mpvVideoOutput = "gpu-next";
    [ObservableProperty] private string _mpvGpuApi = "auto";
    [ObservableProperty] private string _mpvGpuContext = "auto";
    [ObservableProperty] private string _mpvRuntimeInfo = "hwdec: -, interop: -, vo: -, context: -, decoder: -";
    [ObservableProperty] private int _wheelVolumeStep = 5;
    [ObservableProperty] private int _seekStep = 1;
    [ObservableProperty] private PlayerMouseActionOption? _leftClickAction;
    [ObservableProperty] private PlayerMouseActionOption? _rightClickAction;
    [ObservableProperty] private PlayerMouseActionOption? _middleClickAction;
    [ObservableProperty] private PlayerWheelActionOption? _wheelUpAction;
    [ObservableProperty] private PlayerWheelActionOption? _wheelDownAction;
}
