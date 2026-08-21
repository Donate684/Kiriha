using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Kiriha.Mpv;
using Kiriha.Mpv.UI.Services.Player;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    private int _isPlaybackStateUpdatePending;
    private readonly object _playbackStateLock = new();
    private PlaybackState? _pendingPlaybackState;

    private void OnPlayerFileLoaded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsLoading = false;
            HasPlaybackError = false;
            PlaybackErrorMessage = string.Empty;
            PlaybackStatusMessage = _localizer.GetLoc("player.state.ready");
            UpdateNavigationAvailability();
            RefreshDurationFromPlayer();
            UpdateTracks();
            _timelinePreview.WarmUp(VideoUrl);
            _statePublisher.Publish();

            if (SmartTrackAutoload)
            {
                var url = VideoUrl;
                _ = Task.Run(() => LoadSmartTracks(url));
            }
            _ = Task.Run(() =>
            {
                var info = _playback.GetRuntimeVideoInfo();
                Dispatcher.UIThread.Post(() => MpvRuntimeInfo = info);
            });
        });
    }

    private void LoadSmartTracks(string videoPath)
    {
        var matches = SmartTrackAutoloader.FindMatchingTracks(videoPath);
        if (!matches.HasAny)
            return;

        foreach (var subPath in matches.SubtitlePaths)
            _playback.AddSubtitle(subPath);

        foreach (var audioPath in matches.AudioPaths)
            _playback.AddAudioTrack(audioPath);
    }

    private void OnPlayerTracksChanged()
    {
        Dispatcher.UIThread.Post(() => UpdateTracks());
    }

    private void OnPlayerPlaybackEnded(object? sender, MpvPlaybackEndedEventArgs e)
    {
        if (!e.StopsPlayback)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            IsLoading = false;
            IsPlaying = false;
            if (e.HasError)
            {
                HasPlaybackError = true;
                PlaybackErrorMessage = string.IsNullOrWhiteSpace(e.ErrorMessage)
                    ? _localizer.GetLoc("player.state.error_open")
                    : e.ErrorMessage;
                PlaybackStatusMessage = _localizer.GetLoc("player.state.error_playback");
            }

            _statePublisher.Publish();
        });
    }

    private void OnPlayerPlaybackStateChanged(PlaybackState state)
    {
        lock (_playbackStateLock)
        {
            _pendingPlaybackState = state;
        }

        if (System.Threading.Interlocked.CompareExchange(ref _isPlaybackStateUpdatePending, 1, 0) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                System.Threading.Interlocked.Exchange(ref _isPlaybackStateUpdatePending, 0);
                PlaybackState? pendingState;
                lock (_playbackStateLock)
                {
                    pendingState = _pendingPlaybackState;
                }

                if (pendingState != null)
                {
                    ApplyPlaybackState(pendingState);
                }
            });
        }
    }

    private void ApplyPlaybackState(PlaybackState state)
    {
        PlayerTimelineSnapshot? snapshot = null;

        if (state.Duration > 0 && _timeline.TrySetDuration(state.Duration, out var durationSnapshot))
            snapshot = durationSnapshot;

        if (_timeline.TryApplyPlayerTime(state.Position, out var positionSnapshot))
            snapshot = positionSnapshot;

        if (snapshot.HasValue)
            ApplyTimelineSnapshot(snapshot.Value);

        IsPlaying = state.IsPlaying;
    }

    private void ApplyTimelineSnapshot(PlayerTimelineSnapshot snapshot)
    {
        CurrentTime = snapshot.CurrentTime;
        Duration = snapshot.Duration;
        CurrentTimeString = snapshot.CurrentTimeString;
        DurationString = snapshot.DurationString;
    }

    private void RefreshMpvRuntimeInfo()
    {
        if (!_playback.HasPlayer)
            return;

        _ = Task.Run(() =>
        {
            var info = _playback.GetRuntimeVideoInfo();
            Dispatcher.UIThread.Post(() => MpvRuntimeInfo = info);
        });
    }
}
