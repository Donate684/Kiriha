using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.ViewModels.Player;

public partial class PlayerViewModel
{
    [ObservableProperty] private string _subtitleStyleHotkey = "U";
    [ObservableProperty] private string _screenshotWithSubtitlesHotkey = "S";
    [ObservableProperty] private string _screenshotWithoutSubtitlesHotkey = "Shift+S";
    [ObservableProperty] private string _togglePlayPauseHotkey = "Space";
    [ObservableProperty] private string _toggleFullscreenHotkey = "F";
    [ObservableProperty] private string _exitFullscreenHotkey = "Escape";
    [ObservableProperty] private string _toggleMuteHotkey = "M";
    [ObservableProperty] private string _cycleAudioHotkey = "A";
    [ObservableProperty] private string _cycleSubtitleHotkey = "V";
    [ObservableProperty] private string _previousMediaHotkey = "P";
    [ObservableProperty] private string _nextMediaHotkey = "N";
    [ObservableProperty] private string _speedDownHotkey = "OemOpenBrackets";
    [ObservableProperty] private string _speedUpHotkey = "OemCloseBrackets";
    [ObservableProperty] private string _volumeUpHotkey = "Up";
    [ObservableProperty] private string _volumeDownHotkey = "Down";
    [ObservableProperty] private string _seekBackwardHotkey = "Left";
    [ObservableProperty] private string _seekForwardHotkey = "Right";
    [ObservableProperty] private string _reloadSubtitlesHotkey = "Q";
    [ObservableProperty] private string _frameStepForwardHotkey = "OemPeriod";
    [ObservableProperty] private string _frameStepBackwardHotkey = "OemComma";
    [ObservableProperty] private bool _enableSonokoIntegration;
    [ObservableProperty] private string _sonokoIntegrationHotkey = "T";
}
