using System;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    [RelayCommand]
    private void TogglePlayPause()
    {
        if (!_playback.HasPlayer) return;

        if (IsPlaying)
            _playback.Pause();
        else
            _playback.Play();
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    [RelayCommand]
    private void CycleSubtitle()
    {
        _playback.CycleSubtitle();
    }

    [RelayCommand]
    private void CycleAudio()
    {
        _playback.CycleAudio();
    }

    [RelayCommand]
    private void ReloadSubtitles()
    {
        _playback.ReloadSubtitles();
        ShowOsd("Ð¡ÑƒÐ±Ñ‚Ð¸Ñ‚Ñ€Ñ‹", "Ð¿ÐµÑ€ÐµÐ·Ð°Ð³Ñ€ÑƒÐ¶ÐµÐ½Ñ‹");
    }

    [RelayCommand]
    private void FrameStepForward()
    {
        _playback.FrameStep();
    }

    [RelayCommand]
    private void FrameStepBackward()
    {
        _playback.FrameBackStep();
    }

    [RelayCommand]
    public void ToggleSubtitleStyleOverride()
    {
        SubtitleStyleOverrideEnabled = !SubtitleStyleOverrideEnabled;
    }

    [RelayCommand]
    public void MoveSubtitleUp()
    {
        _playback.AdjustSubtitlePosition(-1);
        ShowOsd("Ð¡ÑƒÐ±Ñ‚Ð¸Ñ‚Ñ€Ñ‹", "Ð²Ñ‹ÑˆÐµ");
    }

    [RelayCommand]
    public void MoveSubtitleDown()
    {
        _playback.AdjustSubtitlePosition(1);
        ShowOsd("Ð¡ÑƒÐ±Ñ‚Ð¸Ñ‚Ñ€Ñ‹", "Ð½Ð¸Ð¶Ðµ");
    }

    [RelayCommand]
    private void TakeScreenshot()
    {
        TakeScreenshot(includeSubtitles: false);
    }

    public void TakeScreenshot(bool includeSubtitles)
    {
        _playback.TakeScreenshot(includeSubtitles, ScreenshotResolution?.Value ?? "video");
        ShowOsd("Ð¡ÐºÑ€Ð¸Ð½ÑˆÐ¾Ñ‚", includeSubtitles ? "Ñ ÑÑƒÐ±Ñ‚Ð¸Ñ‚Ñ€Ð°Ð¼Ð¸" : "Ð±ÐµÐ· ÑÑƒÐ±Ñ‚Ð¸Ñ‚Ñ€Ð¾Ð²");
    }

    [RelayCommand]
    private void SetSpeed(object parameter)
    {
        if (parameter != null && double.TryParse(parameter.ToString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double speed))
        {
            PlaybackSpeed = speed;
        }
    }


    [RelayCommand]
    private void Skip(object parameter)
    {
        if (_playback.HasPlayer && parameter != null && int.TryParse(parameter.ToString(), out int seconds))
        {
            var snapshot = _timeline.SeekTo(CurrentTime + seconds);
            _playback.Seek(snapshot.CurrentTime);
            ApplyTimelineSnapshot(snapshot);
        }
    }

    [RelayCommand]
    public void OpenPreviousMedia()
    {
        if (!string.IsNullOrEmpty(_previousMediaPath))
            LoadVideo(_previousMediaPath);
    }

    [RelayCommand]
    public void OpenNextMedia()
    {
        if (!string.IsNullOrEmpty(_nextMediaPath))
            LoadVideo(_nextMediaPath);
    }

    [RelayCommand]
    public void ReloadMedia()
    {
        if (!string.IsNullOrWhiteSpace(VideoUrl))
            LoadVideo(VideoUrl);
    }

    public void SeekTo(double time)
    {
        if (_playback.HasPlayer)
        {
            var snapshot = _timeline.SeekTo(time);
            _playback.Seek(snapshot.CurrentTime);
            ApplyTimelineSnapshot(snapshot);
        }
    }

    public void SeekRelative(double seconds)
    {
        SeekTo(CurrentTime + seconds);
        ShowOsd(seconds >= 0 ? "Ð’Ð¿ÐµÑ€Ñ‘Ð´" : "ÐÐ°Ð·Ð°Ð´", $"{Math.Abs(seconds):0.#} ÑÐµÐº");
    }

    public void AdjustVolume(double delta)
    {
        Volume = Math.Clamp(Volume + delta, 0, 100);
    }

    public void AdjustPlaybackSpeed(double delta)
    {
        PlaybackSpeed = Math.Clamp(PlaybackSpeed + delta, 0.25, 2.0);
    }

    public void ShowTimelinePreview(double timeSeconds, double left)
    {
        _timelinePreview.Show(VideoUrl, Duration, timeSeconds, left);
    }

    public void HideTimelinePreview()
    {
        _timelinePreview.Hide();
    }

    partial void OnSmartTrackAutoloadChanged(bool value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(
            settings => settings.Player.SmartTrackAutoload = value,
            SettingsSection.Player);
    }
}
