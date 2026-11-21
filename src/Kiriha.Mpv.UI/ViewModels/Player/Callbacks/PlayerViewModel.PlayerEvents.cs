using Avalonia.Threading;
using Kiriha.Core.Domain.Constants;
using Kiriha.Mpv.UI.Services.Player;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public partial class PlayerViewModel
{
    private int _isPlaybackStateUpdatePending;
    private MpvSourceEvent.State? _pendingPlaybackState;
    private bool _isEofReached;
    private double _highestObservedDuration;
    private double? _pendingResumePosition;
    private Action<Action>? _postNativeSourceEvent;

    private void OnNativeSourceEvent(MpvSourceEvent notification)
    {
        var post = _postNativeSourceEvent;
        if (post == null)
            return;

        if (notification is not MpvSourceEvent.State state)
        {
            post(() => ApplyNativeSourceEvent(notification));
            return;
        }

        Volatile.Write(ref _pendingPlaybackState, state);
        if (Interlocked.Exchange(ref _isPlaybackStateUpdatePending, 1) == 0)
        {
            post(() =>
            {
                Interlocked.Exchange(ref _isPlaybackStateUpdatePending, 0);
                var latest = Volatile.Read(ref _pendingPlaybackState);
                if (latest != null)
                    ApplyNativeSourceEvent(latest);
            });
        }
    }

    internal void ApplyNativeSourceEvent(MpvSourceEvent notification)
    {
        if (!IsCurrentNativeSource(notification.SourceId))
            return;

        switch (notification)
        {
            case MpvSourceEvent.Loaded loaded:
                ApplyFileLoaded(loaded);
                break;
            case MpvSourceEvent.Ended ended when ended.Details.StopsPlayback:
                ApplyPlaybackEnded(ended);
                break;
            case MpvSourceEvent.State state when state.Playback.IsLoaded && !IsLoading && !HasPlaybackError:
                ApplyPlaybackState(state.Playback);
                break;
            case MpvSourceEvent.Tracks when !IsLoading && !HasPlaybackError:
                UpdateTracks();
                break;
            case MpvSourceEvent.SubtitleDelay delay:
                _subtitleDelay = delay.Seconds;
                break;
            case MpvSourceEvent.Eof eof when !HasPlaybackError:
                ApplyEofReached(eof);
                break;
            case MpvSourceEvent.Buffering buffering when !HasPlaybackError:
                ApplyPlayerBuffering(buffering.Active);
                break;
        }
    }

    private void ApplyFileLoaded(MpvSourceEvent.Loaded loaded)
    {
        _openingState = PlaybackOpeningState.Ready;
        ReleaseReplacedTorrents(_selectedTorrent);
        _isEofReached = false;
        _highestObservedDuration = 0;
        _pendingResumePosition = null;
        HasPlaybackError = false;
        PlaybackErrorMessage = string.Empty;
        PlaybackStatusMessage = _localizer.GetLoc("player.state.ready");
        _subtitleDelay = 0;
        UpdateNavigationAvailability();
        ApplyPlaybackState(loaded.Playback);
        IsLoading = false;
        UpdateTracks();
        _timelinePreview.WarmUp(VideoUrl);

        if (RememberPlaybackPosition)
            CheckAndPromptResume();

        _skippedSegments.Clear();
        Overlay.DismissSkipPrompt(smooth: false);
        RefreshSkipSegments();

        if (!string.IsNullOrWhiteSpace(_pendingSubtitleUrl))
        {
            var subtitle = _pendingSubtitleUrl;
            _pendingSubtitleUrl = null;
            _playback.AddSubtitle(subtitle, select: true);
        }

        if (SmartTrackAutoload)
            _ = LoadSmartTracksAsync(VideoUrl, loaded.SourceId);

        RefreshMpvRuntimeInfo();
        _statePublisher.Publish();
    }

    private async Task LoadSmartTracksAsync(string videoPath, long sourceId)
    {
        try
        {
            var matches = await Task.Run(() => SmartTrackAutoloader.FindMatchingTracks(videoPath), CancellationToken.None)
                .ConfigureAwait(false);
            if (!matches.HasAny)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentNativeSource(sourceId) || HasPlaybackError)
                    return;

                foreach (var path in matches.SubtitlePaths)
                    _playback.AddSubtitle(path);

                foreach (var path in matches.AudioPaths)
                    _playback.AddAudioTrack(path);
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Could not auto-load tracks for the current playback source");
        }
    }

    private void CheckAndPromptResume()
    {
        if (string.IsNullOrWhiteSpace(VideoUrl) || !RememberPlaybackPosition)
            return;

        var watchLaterDir = Kiriha.Infrastructure.Platform.PathHelper.GetMpvWatchLaterPath();
        var savedPos = _resumeStore?.TryGetResumePosition(VideoUrl, watchLaterDir)
                       ?? WatchLaterHelper.TryGetSavedPosition(watchLaterDir, VideoUrl);

        if (savedPos is >= 5 && double.IsFinite(savedPos.Value))
        {
            _pendingResumePosition = savedPos;
            TryShowPendingResume();
        }
    }

    private void TryShowPendingResume()
    {
        if (_pendingResumePosition is not double position)
            return;

        if (!RememberPlaybackPosition || CurrentTime >= position)
        {
            _pendingResumePosition = null;
            return;
        }

        // A stream can report a partial duration at FILE_LOADED and extend it as data arrives.
        if (Duration > 0 && position >= Duration * 0.95)
            return;

        _pendingResumePosition = null;
        Overlay.ShowResumePrompt(position, PlayerTimelineService.FormatTime(position));
    }

    private bool CanSaveCurrentPosition() => RememberPlaybackPosition && !IsLoading && !HasPlaybackError &&
        !_isEofReached && Duration > 0 && CurrentTime > 0;

    private void ApplyPlaybackEnded(MpvSourceEvent.Ended ended)
    {
        ReleaseReplacedTorrents(_selectedTorrent);
        var playback = ended.Playback;
        var endedAt = playback.Position > 0 ? playback.Position : CurrentTime;
        var duration = Math.Max(_highestObservedDuration, playback.Duration);
        var prematureStreamEnd = ended.Details.Reason == MpvPlaybackEndedEventArgs.ReasonEof &&
            IsRemoteSource() && duration > 0 && endedAt < duration * 0.95;
        var failed = ended.Details.HasError || prematureStreamEnd;
        if (failed)
        {
            FailPlayback(endedAt, duration, ended.Details.ErrorMessage);
            return;
        }
        else if (ended.Details.Reason == MpvPlaybackEndedEventArgs.ReasonEof && CanCompletePlayback(endedAt, duration))
        {
            CompletePlayback();
        }

        StopPlaybackState();
    }

    private void ApplyEofReached(MpvSourceEvent.Eof eof)
    {
        var duration = Math.Max(_highestObservedDuration, eof.Playback.Duration);
        if (!eof.Reached)
        {
            _isEofReached = false;
            _playback.SetSavePositionOnQuit(RememberPlaybackPosition);
        }
        else if (CanCompletePlayback(eof.Playback.Position, duration))
        {
            CompletePlayback();
        }
        else if (duration > 0)
        {
            // With keep-open, a prematurely exhausted network stream may only signal eof-reached.
            FailPlayback(eof.Playback.Position, duration, errorMessage: null);
        }
    }

    private void FailPlayback(double position, double duration, string? errorMessage)
    {
        if (!HasPlaybackError && RememberPlaybackPosition && position >= 5 &&
            (duration <= 0 || position < duration * 0.95))
        {
            _resumeStore?.SavePosition(VideoUrl, position, duration);
        }

        HasPlaybackError = true;
        PlaybackErrorMessage = errorMessage ?? _localizer.GetLoc(AppConstants.Player.OpenFailedLocKey);
        PlaybackStatusMessage = _localizer.GetLoc("player.state.error_playback");
        _updateTracksCts?.Cancel();
        StopPlaybackState();
    }

    private void StopPlaybackState()
    {
        if (_openingState == PlaybackOpeningState.Pending)
            _openingState = PlaybackOpeningState.Failed;
        DismissResumePrompt();
        Overlay.DismissSkipPrompt(smooth: false);
        ApplyPlayerBuffering(false);
        IsPlaying = false;
        IsLoading = false;
        _statePublisher.Publish();
    }

    private bool CanCompletePlayback(double position, double duration) =>
        !IsRemoteSource() || duration > 0 && position >= duration * 0.95;

    private bool IsRemoteSource() =>
        Uri.TryCreate(VideoUrl, UriKind.Absolute, out var uri) && !uri.IsFile;

    private void CompletePlayback()
    {
        if (_isEofReached)
            return;

        _isEofReached = true;
        DismissResumePrompt();
        Overlay.DismissSkipPrompt(smooth: false);
        if (RememberPlaybackPosition)
        {
            _resumeStore?.Remove(VideoUrl);
            _playback.DeleteWatchLaterConfig();
            _playback.SetSavePositionOnQuit(false);
        }
    }

    private void ApplyPlaybackState(PlaybackState state)
    {
        PlayerTimelineSnapshot? snapshot = null;

        // A demuxer can shorten the reported duration when a network response ends early.
        _highestObservedDuration = Math.Max(_highestObservedDuration, state.Duration);
        if (state.Duration > 0 && _timeline.TrySetDuration(state.Duration, out var durationSnapshot))
            snapshot = durationSnapshot;

        if (_timeline.TryApplyPlayerTime(state.Position, out var positionSnapshot))
            snapshot = positionSnapshot;

        if (snapshot.HasValue)
            ApplyTimelineSnapshot(snapshot.Value);

        TryShowPendingResume();
        IsPlaying = state.IsPlaying;
    }

    private void ApplyTimelineSnapshot(PlayerTimelineSnapshot snapshot)
    {
        CurrentTime = snapshot.CurrentTime;
        Duration = snapshot.Duration;
        CurrentTimeString = snapshot.CurrentTimeString;
        DurationString = snapshot.DurationString;
        CheckSkipSegments(snapshot.CurrentTime, snapshot.Duration);
    }

    private void RefreshMpvRuntimeInfo()
    {
        if (!_playback.HasPlayer)
            return;

        var sourceId = _nativeSourceId;
        _ = Task.Run(() =>
        {
            var info = _playback.GetRuntimeVideoInfo();
            Dispatcher.UIThread.Post(() =>
            {
                if (IsCurrentNativeSource(sourceId))
                    MpvRuntimeInfo = info;
            });
        }, CancellationToken.None);
    }
}
