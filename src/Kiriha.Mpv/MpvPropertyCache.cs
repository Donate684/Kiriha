using System;

namespace Kiriha.Mpv;

internal sealed class MpvPropertyCache
{
    private const double TimePositionMinimumChangeSeconds = 0.05;
    private static readonly TimeSpan RuntimeInfoRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TimePositionEventInterval = TimeSpan.FromMilliseconds(80);

    private readonly object _gate = new();
    private volatile PlaybackState _playbackState = new(0, 0, false, false, false);
    private double _lastPublishedTimePosition;
    private DateTime _lastTimePositionEventUtc = DateTime.MinValue;
    private volatile string _runtimeVideoInfo;
    private DateTime _runtimeVideoInfoRefreshedUtc = DateTime.MinValue;
    private volatile bool _runtimeVideoInfoDirty = true;

    public MpvPropertyCache(string initialRuntimeVideoInfo)
    {
        _runtimeVideoInfo = initialRuntimeVideoInfo;
    }

    public double TimePosition => _playbackState.Position;
    public double Duration => _playbackState.Duration;
    public bool IsPaused => !_playbackState.IsPlaying;
    public string RuntimeVideoInfo => _runtimeVideoInfo;
    public PlaybackState PlaybackState => _playbackState;

    public bool HasFreshRuntimeVideoInfo
    {
        get
        {
            lock (_gate)
            {
                return !_runtimeVideoInfoDirty &&
                       DateTime.UtcNow - _runtimeVideoInfoRefreshedUtc < RuntimeInfoRefreshInterval;
            }
        }
    }

    public void StoreRuntimeVideoInfo(string info)
    {
        lock (_gate)
        {
            _runtimeVideoInfo = info;
            _runtimeVideoInfoRefreshedUtc = DateTime.UtcNow;
            _runtimeVideoInfoDirty = false;
        }
    }

    public void InvalidateRuntimeVideoInfo()
    {
        lock (_gate)
        {
            _runtimeVideoInfoDirty = true;
        }
    }

    public bool TryUpdateTimePosition(double timePosition)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var isFirstEvent = _lastTimePositionEventUtc == DateTime.MinValue;
            var changedEnough = Math.Abs(timePosition - _lastPublishedTimePosition) >= TimePositionMinimumChangeSeconds;
            var elapsedEnough = now - _lastTimePositionEventUtc >= TimePositionEventInterval;

            var oldState = _playbackState;
            _playbackState = oldState with { Position = timePosition };

            if (!isFirstEvent && !changedEnough && !elapsedEnough)
                return false;

            _lastPublishedTimePosition = timePosition;
            _lastTimePositionEventUtc = now;
            return true;
        }
    }

    public bool TryUpdateDuration(double duration)
    {
        lock (_gate)
        {
            var oldState = _playbackState;
            if (Math.Abs(duration - oldState.Duration) <= 0.01)
                return false;

            _playbackState = oldState with { Duration = duration };
            return true;
        }
    }

    public bool TryUpdatePause(bool isPaused)
    {
        lock (_gate)
        {
            var oldState = _playbackState;
            if (!isPaused == oldState.IsPlaying)
                return false;

            _playbackState = oldState with { IsPlaying = !isPaused };
            return true;
        }
    }

    public bool TryUpdateSeekable(bool isSeekable)
    {
        lock (_gate)
        {
            var oldState = _playbackState;
            if (isSeekable == oldState.IsSeekable)
                return false;

            _playbackState = oldState with { IsSeekable = isSeekable };
            return true;
        }
    }

    public bool TryUpdateLoaded(bool isLoaded)
    {
        lock (_gate)
        {
            var oldState = _playbackState;
            if (isLoaded == oldState.IsLoaded)
                return false;

            _playbackState = oldState with { IsLoaded = isLoaded };
            return true;
        }
    }

    public bool TryUpdatePlaybackEnded()
    {
        lock (_gate)
        {
            var oldState = _playbackState;
            var changed = oldState.IsPlaying;
            _playbackState = oldState with { IsPlaying = false };
            return changed;
        }
    }
}
