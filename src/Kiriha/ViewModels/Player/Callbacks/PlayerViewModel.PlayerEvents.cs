using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Kiriha.Mpv;

namespace Kiriha.ViewModels.Player;

public partial class PlayerViewModel
{
    private int _isPlaybackStateUpdatePending;
    private PlaybackState? _pendingPlaybackState;

    private void OnPlayerFileLoaded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsLoading = false;
            HasPlaybackError = false;
            PlaybackErrorMessage = string.Empty;
            PlaybackStatusMessage = "Готово";
            UpdateNavigationAvailability();
            RefreshDurationFromPlayer();
            UpdateTracks();
            _timelinePreview.WarmUp(VideoUrl);
            _statePublisher.Publish();
            _ = Task.Run(() =>
            {
                var info = _playback.GetRuntimeVideoInfo();
                Dispatcher.UIThread.Post(() => MpvRuntimeInfo = info);
            });
        });
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
                    ? "Не удалось открыть или воспроизвести файл."
                    : e.ErrorMessage;
                PlaybackStatusMessage = "Ошибка воспроизведения";
            }

            _statePublisher.Publish();
        });
    }

    private void OnPlayerPlaybackStateChanged(PlaybackState state)
    {
        _pendingPlaybackState = state;
        if (System.Threading.Interlocked.CompareExchange(ref _isPlaybackStateUpdatePending, 1, 0) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                System.Threading.Interlocked.Exchange(ref _isPlaybackStateUpdatePending, 0);
                if (_pendingPlaybackState is { } pendingState)
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

        MpvRuntimeInfo = _playback.GetRuntimeVideoInfo();
    }
}
