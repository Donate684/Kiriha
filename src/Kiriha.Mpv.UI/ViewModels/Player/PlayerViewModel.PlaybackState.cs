using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    [ObservableProperty] private bool _isPlaying = true;
    [ObservableProperty] private double _currentTime = 0;
    [ObservableProperty] private double _duration = 0;
    [ObservableProperty] private string _currentTimeString = "00:00";
    [ObservableProperty] private string _durationString = "--:--";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasPlaybackError;
    [ObservableProperty] private string _playbackStatusMessage = "Загрузка видео...";
    [ObservableProperty] private string _playbackErrorMessage = string.Empty;
    [ObservableProperty] private bool _canOpenPreviousMedia;
    [ObservableProperty] private bool _canOpenNextMedia;

    [ObservableProperty] private double _volume = 100;
    [ObservableProperty] private bool _isMuted = false;
    [ObservableProperty] private bool _normalizeAudio = false;
    private double _previousVolume = 100;

    [ObservableProperty] private double _playbackSpeed = 1.0;
}
