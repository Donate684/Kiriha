using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Mpv.UI.Services.Player;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    [RelayCommand]
    private void TogglePlayPause()
    {
        if (!_playback.HasPlayer) return;

        bool shouldPlay = !IsPlaying;
        IsPlaying = shouldPlay;
        if (shouldPlay)
            _playback.Play();
        else
            _playback.Pause();
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
        ShowOsd(_localizer.GetLoc("player.osd.subtitles"), _localizer.GetLoc("player.osd.reloaded"));
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
        ShowOsd(_localizer.GetLoc("player.osd.subtitles"), _localizer.GetLoc("player.osd.higher"));
    }

    [RelayCommand]
    public void MoveSubtitleDown()
    {
        _playback.AdjustSubtitlePosition(1);
        ShowOsd(_localizer.GetLoc("player.osd.subtitles"), _localizer.GetLoc("player.osd.lower"));
    }

    [RelayCommand]
    public void AdjustSubtitleDelay(double delta)
    {
        _subtitleDelay = Math.Round(_subtitleDelay + delta, 3);
        _playback.AdjustSubtitleDelay(delta);
        ShowSubtitleDelayOsd(_subtitleDelay);
    }

    private void ShowSubtitleDelayOsd(double delaySeconds)
    {
        int delayMs = (int)Math.Round(delaySeconds * 1000);
        string sign = delayMs > 0 ? "+" : "";
        ShowOsd(_localizer.GetLoc("player.osd.sub_delay"), $"{sign}{delayMs} {_localizer.GetLoc("player.osd.ms")}");
    }

    [RelayCommand]
    private void TakeScreenshot()
    {
        TakeScreenshot(includeSubtitles: false);
    }

    public void TakeScreenshot(bool includeSubtitles)
    {
        _playback.TakeScreenshot(includeSubtitles, ScreenshotResolution?.Value ?? "video");
        ShowOsd(_localizer.GetLoc("player.osd.screenshot"), includeSubtitles ? _localizer.GetLoc("player.osd.with_subtitles") : _localizer.GetLoc("player.osd.without_subtitles"));
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

    [RelayCommand]
    public void ResumeFromSavedPosition()
    {
        if (Overlay.IsResumePromptVisible && Overlay.ResumePromptPosition > 0)
        {
            var pos = Overlay.ResumePromptPosition;
            Overlay.DismissResumePrompt();
            if (_playback.HasPlayer)
            {
                SeekTo(pos);
                ShowOsd(_localizer.GetLoc("player.osd.resumed"), PlayerTimelineService.FormatTime(pos));
            }
        }
    }

    [RelayCommand]
    public void DismissResumePrompt()
    {
        Overlay.DismissResumePrompt();
    }

    [RelayCommand]
    public void SkipActiveSegment()
    {
        var segment = Overlay.ActiveSkipSegment;
        Overlay.DismissSkipPrompt(smooth: false);
        if (segment != null)
        {
            _skippedSegments.Add(segment);
            if (_playback.HasPlayer)
            {
                var target = SkipMarkerResolver.GetSeekTarget(segment, Duration);
                SeekTo(target);
                NotifySegmentSkipped(segment);
            }
        }
    }

    [RelayCommand]
    public void DismissSkipPrompt()
    {
        var segment = Overlay.ActiveSkipSegment;
        if (segment != null)
        {
            _skippedSegments.Add(segment);
        }
        Overlay.DismissSkipPrompt(smooth: true);
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
        ShowOsd(seconds >= 0 ? _localizer.GetLoc("player.osd.forward") : _localizer.GetLoc("player.osd.backward"), $"{Math.Abs(seconds):0.#} {_localizer.GetLoc("player.osd.seconds")}");
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
        string? segmentLabel = null;
        var activeSegment = _skipSegments.FirstOrDefault(s => timeSeconds >= s.StartSeconds && timeSeconds <= s.EndSeconds);
        if (activeSegment != null)
        {
            segmentLabel = GetSegmentTitle(activeSegment.Type);
        }

        _timelinePreview.Show(VideoUrl, Duration, timeSeconds, left, segmentLabel);
    }

    public string GetSegmentTitle(SkipSegmentType type) => type switch
    {
        SkipSegmentType.Intro => _localizer.GetLoc("player.skip.op_title"),
        SkipSegmentType.Outro => _localizer.GetLoc("player.skip.ed_title"),
        SkipSegmentType.MixedIntro => _localizer.GetLoc("player.skip.mixed_op_title"),
        SkipSegmentType.MixedOutro => _localizer.GetLoc("player.skip.mixed_ed_title"),
        SkipSegmentType.Recap => _localizer.GetLoc("player.skip.recap_title"),
        _ => _localizer.GetLoc("player.skip.preview_title")
    };

    public void HideTimelinePreview()
    {
        _timelinePreview.Hide();
    }

    partial void OnSmartTrackAutoloadChanged(bool value)
    {
        if (_isApplyingSettings || _settingsService is null) return;
        _settingsService.Update(
            settings => settings.Player.SmartTrackAutoload = value,
            SettingsSection.Player);
    }

    private void NotifySegmentSkipped(SkipSegment segment)
    {
        var segmentName = GetSegmentTitle(segment.Type);
        var message = string.Format(_localizer.GetLoc("player.skip.osd_skipped"), segmentName);
        ShowOsd(message);
        Overlay.ShowSkipToast(message);
    }

}
