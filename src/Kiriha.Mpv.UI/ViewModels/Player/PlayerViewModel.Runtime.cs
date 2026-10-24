using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Mpv.UI.Services.Player;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    public void Initialize(MpvPlayer player)
    {
        _playback.Attach(player);
        _playback.FileLoaded += OnPlayerFileLoaded;
        _playback.PlaybackEnded += OnPlayerPlaybackEnded;
        _playback.PlaybackStateChanged += OnPlayerPlaybackStateChanged;
        _playback.TracksChanged += OnPlayerTracksChanged;
        _playback.SubtitleDelayChanged += OnPlayerSubtitleDelayChanged;
        _playback.EofReachedChanged += OnPlayerEofReachedChanged;
        _playback.SetVolume(Volume);
        _playback.SetSpeed(PlaybackSpeed);
        _playback.SetAudioNormalization(NormalizeAudio);
        ApplyTrackLanguagePreferences();
        ApplyVideoProcessingOptions();
        ApplyScreenshotOptions();
        ApplySubtitleStyleOverride();
        _timelinePreview.Initialize();
        _timelinePreview.WarmUp(VideoUrl);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTimerTick;
        if (IsPlaying)
        {
            _timer.Start();
        }

        _statePublisher.Connect();
    }

    public void LoadSubtitle(string path, bool select = true)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        _playback.AddSubtitle(path, select);
    }

    public void LoadVideo(string videoUrl, string? subtitleUrl = null, PlayerMediaMetadata? metadata = null)
    {
        if (subtitleUrl != null)
        {
            _pendingSubtitleUrl = subtitleUrl;
        }
        else if (!string.Equals(VideoUrl, videoUrl, StringComparison.Ordinal))
        {
            _pendingSubtitleUrl = null;
        }

        if (string.IsNullOrWhiteSpace(videoUrl))
            return;

        videoUrl = videoUrl.Trim().Trim('"');
        try
        {
            if (System.IO.Path.IsPathRooted(videoUrl))
            {
                videoUrl = System.IO.Path.GetFullPath(videoUrl);
            }
        }
        catch
        {
            // Ignore path normalization failure for URLs/streams
        }

        if (_playback.HasPlayer && !string.IsNullOrWhiteSpace(VideoUrl))
        {
            if (RememberPlaybackPosition && !_isEofReached && Duration > 0 && CurrentTime > 0)
            {
                if (CurrentTime < Duration * 0.95 && CurrentTime >= 5)
                {
                    PlaybackResumeStore.SavePosition(VideoUrl, CurrentTime, Duration);
                    _playback.WriteWatchLaterConfig();
                }
                else
                {
                    PlaybackResumeStore.Remove(VideoUrl);
                    _playback.DeleteWatchLaterConfig();
                }
            }
        }
        Overlay.DismissResumePrompt();
        _isEofReached = false;

        if (metadata != null)
        {
            _hasExplicitMetadata = true;
            _previousVideoUrlForMetadata = videoUrl;
            _suppressVideoUrlResolution = true;
            VideoUrl = videoUrl;
            ApplyMetadata(metadata, overwriteAll: true);
            UpdateNavigationAvailability();
        }
        else if (!string.Equals(VideoUrl, videoUrl, StringComparison.Ordinal))
        {
            _hasExplicitMetadata = false;
            VideoUrl = videoUrl;
        }
        else if (!_hasExplicitMetadata && string.IsNullOrWhiteSpace(AnimeTitle))
        {
            ResolveAndApplyMetadata(videoUrl);
        }

        InitializeTorrentStream(videoUrl, metadata);

        ApplyTimelineSnapshot(_timeline.Reset());
        _subtitleDelay = 0;
        IsPlaying = PlayerAutoPlay;
        IsLoading = true;
        HasPlaybackError = false;
        PlaybackErrorMessage = string.Empty;
        PlaybackStatusMessage = _localizer.GetLoc("player.state.loading");

        if (!_playback.HasPlayer)
            return;

        if (!PlayerAutoPlay)
            _playback.Pause();

        _playback.Load(videoUrl);

        if (PlayerAutoPlay)
            _playback.Play();

        _statePublisher.Publish();
    }

    private double _lastPublishedPosition = -1;
    private bool _lastPublishedIsPlaying;

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!_playback.HasPlayer) return;

        if (IsPlaying || _lastPublishedIsPlaying != IsPlaying || Math.Abs(_lastPublishedPosition - CurrentTime) > 0.5)
        {
            _statePublisher.Publish();
            _lastPublishedPosition = CurrentTime;
            _lastPublishedIsPlaying = IsPlaying;
        }

        if (_mpvRuntimeDiagnosticsVisible)
            RefreshMpvRuntimeInfo();
    }

    public void SetMpvRuntimeDiagnosticsVisible(bool visible)
    {
        if (_mpvRuntimeDiagnosticsVisible == visible)
            return;

        _mpvRuntimeDiagnosticsVisible = visible;

        if (visible)
            RefreshMpvRuntimeInfo();
    }

    private void ShowOsd(string message, string detail = "")
    {
        Overlay.ShowOsd(message, detail);
    }

    private CancellationTokenSource? _updateTracksCts;

    public void UpdateTracks()
    {
        var oldCts = _updateTracksCts;
        _updateTracksCts = new CancellationTokenSource();

        try { oldCts?.Cancel(); } catch (ObjectDisposedException) { }
        oldCts?.Dispose();

        _ = UpdateTracksAsync(_updateTracksCts.Token);
    }

    private void RefreshDurationFromPlayer()
    {
        ApplyPlaybackState(_playback.GetPlaybackState());
    }

    private async Task UpdateTracksAsync(CancellationToken token)
    {
        var result = await _playback.GetTracksAndChaptersAsync();
        if (result is null || token.IsCancellationRequested)
            return;

        var (tracks, chapters) = result.Value;

        var audioTracks = tracks.Where(t => t.Type == "audio").ToList();
        var subTracks = tracks.Where(t => t.Type == "sub").ToList();

        if (token.IsCancellationRequested) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested) return;

            AudioTracks.Clear();
            SubtitleTracks.Clear();

            // Add 'None' option for subtitles
            SubtitleTracks.Add(new TrackInfo { Id = "no", Type = "sub", Title = _localizer.GetLoc("player.osd.off") });

            foreach (var t in audioTracks) AudioTracks.Add(t);
            foreach (var t in subTracks) SubtitleTracks.Add(t);

            Chapters.Clear();
            foreach (var ch in chapters)
                Chapters.Add(ch);

            RefreshSkipSegments();
        });
    }

    [RelayCommand]
    private void SelectTrack(TrackInfo track)
    {
        if (_playback.HasPlayer && track != null)
        {
            _playback.SetTrack(track.Type, track.Id);
            ShowOsd(track.Type == "sub" ? _localizer.GetLoc("player.osd.subtitles") : _localizer.GetLoc("player.osd.audio"), track.DisplayName);
        }
    }

    private long _lastScrubSeekTick;

    public bool IsScrubbing => _timeline.IsScrubbing;

    public void BeginScrub()
    {
        _lastScrubSeekTick = 0;
        Overlay.DismissResumePrompt();
        _timeline.BeginScrub();
    }

    public void EndScrub()
    {
        _lastScrubSeekTick = 0;
        if (_playback.HasPlayer)
        {
            var snapshot = _timeline.EndScrub(CurrentTime);
            _playback.Seek(snapshot.CurrentTime, fast: false);
            ApplyTimelineSnapshot(snapshot);
        }
    }

    // Called when CurrentTime is changed via UI while scrubbing
    partial void OnCurrentTimeChanged(double value)
    {
        if (_timeline.IsScrubbing)
        {
            var snapshot = _timeline.UpdateScrubTime(value);
            ApplyTimelineSnapshot(snapshot);

            long now = Environment.TickCount64;
            if (now - _lastScrubSeekTick >= 50)
            {
                _lastScrubSeekTick = now;
                _playback.Seek(snapshot.CurrentTime, fast: true);
            }
        }
    }

    partial void OnVolumeChanged(double value)
    {
        if (IsMuted)
        {
            // Unmute automatically if user drags the slider
            _isMuted = false;
            OnPropertyChanged(nameof(IsMuted));
        }

        if (_playback.HasPlayer)
        {
            _playback.SetVolume(value);
        }

        if (!_isApplyingSettings && RememberPlayerVolume && _settingsService != null)
        {
            _settingsService.Update(settings => settings.Player.Volume = Math.Clamp(value, 0, 100), SettingsSection.Player);
        }

        if (!_isApplyingSettings)
            ShowOsd(_localizer.GetLoc("player.osd.volume"), $"{Math.Clamp(value, 0, 100):0}%");
    }

    partial void OnPlaybackSpeedChanged(double value)
    {
        var speed = Math.Clamp(value, 0.1, 4.0);
        _playback.SetSpeed(speed);

        if (!_isApplyingSettings && _settingsService != null)
        {
            _settingsService.Update(settings => settings.Player.PlaybackSpeed = speed, SettingsSection.Player);
        }

        if (!_isApplyingSettings)
            ShowOsd(_localizer.GetLoc("player.osd.speed"), $"{speed:0.##}x");
    }

    partial void OnNormalizeAudioChanged(bool value)
    {
        _playback.SetAudioNormalization(value);

        if (_isApplyingSettings || _settingsService is null) return;
        _settingsService.Update(settings => settings.Player.NormalizeAudio = value, SettingsSection.Player);
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (!_playback.HasPlayer) return;

        if (value)
        {
            _playback.SetVolume(0);
            ShowOsd(_localizer.GetLoc("player.osd.sound"), _localizer.GetLoc("player.osd.muted"));
        }
        else
        {
            _playback.SetVolume(Volume);
            ShowOsd(_localizer.GetLoc("player.osd.sound"), $"{Math.Clamp(Volume, 0, 100):0}%");
        }
    }
}
