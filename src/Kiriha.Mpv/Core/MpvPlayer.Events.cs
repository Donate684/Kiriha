using System.Runtime.InteropServices;
using Serilog;

namespace Kiriha.Mpv;

public partial class MpvPlayer
{
    private const ulong PropertyIdMask = 15;
    private ulong _observationTag;
    private ulong _lastObservationTag;

    internal long QueueSourceLoad(Action<IntPtr, long> load)
    {
        var sourceId = _playlistSource.Reserve();
        if (!_commandQueue.Enqueue(handle =>
        {
            lock (_sourceEventGate)
            {
                if (!_playlistSource.IsRequested(sourceId))
                    return;

                UnobservePlaybackProperties(handle);
                ResetPropertyCache();
                try
                {
                    load(handle, sourceId);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not submit the mpv load command");
                    PublishEnded(sourceId, new MpvPlaybackEndedEventArgs(MpvPlaybackEndedEventArgs.ReasonError, 0));
                }
            }
        }))
        {
            PublishEnded(sourceId, new MpvPlaybackEndedEventArgs(MpvPlaybackEndedEventArgs.ReasonError, 0));
        }

        return sourceId;
    }

    internal void LoadPlaylistEntry(IntPtr handle, long sourceId, string url, bool? autoPlay)
    {
        if (autoPlay == false)
            LibMpvNative.mpv_set_property_string(handle, "pause", "yes");

        var status = LibMpvNative.CommandWithResult(handle, out var result, "loadfile", url);
        if (status < 0)
        {
            LibMpvNative.mpv_set_property_string(handle, "pause", "yes");
            PublishEnded(sourceId, new MpvPlaybackEndedEventArgs(MpvPlaybackEndedEventArgs.ReasonError, status));
            return;
        }

        try
        {
            if (MpvNodeParser.TryGetMapValue(result, "playlist_entry_id", out var entry) &&
                entry.Format == LibMpvNative.MPV_FORMAT_INT64 && entry.U.Int64 > 0)
            {
                _playlistSource.Register(sourceId, entry.U.Int64);
                if (autoPlay == true && _playlistSource.IsRequested(sourceId))
                    LibMpvNative.mpv_set_property_string(handle, "pause", "no");
                return;
            }

            throw new InvalidOperationException("mpv did not return a playlist entry for the load command.");
        }
        finally
        {
            LibMpvNative.mpv_free_node_contents(ref result);
        }
    }

    private unsafe void HandleEvent(MpvEvent mpvEvent)
    {
        lock (_sourceEventGate)
        {
            switch (mpvEvent.EventId)
            {
                case LibMpvNative.MPV_EVENT_START_FILE when mpvEvent.Data != IntPtr.Zero:
                    Read(UnobservePlaybackProperties);
                    _playlistSource.Start(*(long*)mpvEvent.Data); // mpv_event_start_file contains the playlist entry ID.
                    break;

                case LibMpvNative.MPV_EVENT_FILE_LOADED:
                    var loadedSource = _playlistSource.StartedSource;
                    if (loadedSource != 0)
                        HandleFileLoaded(loadedSource);
                    break;

                case LibMpvNative.MPV_EVENT_END_FILE when mpvEvent.Data != IntPtr.Zero:
                    HandleEndFile(*(MpvEventEndFile*)mpvEvent.Data);
                    break;

                case LibMpvNative.MPV_EVENT_PROPERTY_CHANGE when _observationTag != 0 &&
                    (mpvEvent.ReplyUserData & ~PropertyIdMask) == _observationTag:
                    var observedSource = _playlistSource.StartedSource;
                    if (observedSource != 0)
                        HandlePropertyChange(mpvEvent, observedSource);
                    break;
            }
        }
    }

    private void HandleFileLoaded(long sourceId)
    {
        ResetPropertyCache();
        Read(handle =>
        {
            ObservePlaybackProperties(handle);
            var pauseAvailable = LibMpvNative.mpv_get_property_int(handle, "pause", LibMpvNative.MPV_FORMAT_FLAG, out int paused) >= 0;
            _propertyCache.TryUpdatePause(!pauseAvailable || paused != 0);
            _propertyCache.TryUpdateDuration(GetPlaybackDouble(handle, "duration"));
            _propertyCache.TryUpdateTimePosition(GetPlaybackDouble(handle, "time-pos"));
        });

        _propertyCache.TryUpdateLoaded(true);
        FileLoaded?.Invoke(this, EventArgs.Empty);
        SourceEvent?.Invoke(new MpvSourceEvent.Loaded(sourceId, _propertyCache.PlaybackState));
        PublishPlaybackState(sourceId);
    }

    private static double GetPlaybackDouble(IntPtr handle, string property, double fallback = 0) =>
        LibMpvNative.mpv_get_property(handle, property, LibMpvNative.MPV_FORMAT_DOUBLE, out double value) >= 0 ? value : fallback;

    private void HandleEndFile(MpvEventEndFile endFile)
    {
        bool redirect = endFile.Reason == MpvPlaybackEndedEventArgs.ReasonRedirect;
        var sourceId = _playlistSource.End(endFile.PlaylistEntryId,
            redirect ? endFile.PlaylistInsertId : 0, redirect ? endFile.PlaylistInsertNumEntries : 0);
        if (sourceId == 0)
            return;

        Read(UnobservePlaybackProperties);
        var details = new MpvPlaybackEndedEventArgs(endFile.Reason, endFile.Error);
        if (details.StopsPlayback)
        {
            _propertyCache.TryUpdateLoaded(false);
            _propertyCache.TryUpdatePlaybackEnded();
        }
        PublishEnded(sourceId, details);
        if (details.StopsPlayback)
            PublishPlaybackState(sourceId);
    }

    private void PublishEnded(long sourceId, MpvPlaybackEndedEventArgs details)
    {
        if (!_playlistSource.IsRequested(sourceId))
            return;

        PlaybackEnded?.Invoke(this, details);
        SourceEvent?.Invoke(new MpvSourceEvent.Ended(sourceId, details, _propertyCache.PlaybackState));
    }

    private static readonly (ulong Id, string Name, int Format)[] ObservedProperties =
    [
        (TimePositionPropertyId, "time-pos", LibMpvNative.MPV_FORMAT_DOUBLE),
        (DurationPropertyId, "duration", LibMpvNative.MPV_FORMAT_DOUBLE),
        (PausePropertyId, "pause", LibMpvNative.MPV_FORMAT_FLAG),
        (SeekablePropertyId, "seekable", LibMpvNative.MPV_FORMAT_FLAG),
        (TrackListPropertyId, "track-list", LibMpvNative.MPV_FORMAT_NONE),
        (SubDelayPropertyId, "sub-delay", LibMpvNative.MPV_FORMAT_DOUBLE),
        (EofReachedPropertyId, "eof-reached", LibMpvNative.MPV_FORMAT_FLAG),
        (PausedForCachePropertyId, "paused-for-cache", LibMpvNative.MPV_FORMAT_FLAG)
    ];

    private void ObservePlaybackProperties(IntPtr handle)
    {
        // Old notifications can already be in flight when unobserved. Each registration has new IDs.
        _lastObservationTag += PropertyIdMask + 1;
        _observationTag = _lastObservationTag;
        foreach (var (id, name, format) in ObservedProperties)
            Check(LibMpvNative.mpv_observe_property(handle, _observationTag | id, name, format), $"observe {name}");
    }

    private void UnobservePlaybackProperties(IntPtr handle)
    {
        if (_observationTag == 0)
            return;

        foreach (var (id, _, _) in ObservedProperties)
            LibMpvNative.mpv_unobserve_property(handle, _observationTag | id);
        _observationTag = 0;
    }

    private unsafe void HandlePropertyChange(MpvEvent mpvEvent, long sourceId)
    {
        if (mpvEvent.Data == IntPtr.Zero)
            return;

        var property = *(MpvEventProperty*)mpvEvent.Data;
        var propertyId = mpvEvent.ReplyUserData & PropertyIdMask;
        if (propertyId == TrackListPropertyId)
        {
            TracksChanged?.Invoke();
            SourceEvent?.Invoke(new MpvSourceEvent.Tracks(sourceId));
            return;
        }

        if (property.Format == LibMpvNative.MPV_FORMAT_NONE || property.Data == IntPtr.Zero)
            return;

        switch (propertyId)
        {
            case TimePositionPropertyId when property.Format == LibMpvNative.MPV_FORMAT_DOUBLE:
                if (_propertyCache.TryUpdateTimePosition(*(double*)property.Data))
                    PublishPlaybackState(sourceId);
                break;

            case DurationPropertyId when property.Format == LibMpvNative.MPV_FORMAT_DOUBLE:
                if (_propertyCache.TryUpdateDuration(*(double*)property.Data))
                    PublishPlaybackState(sourceId);
                break;

            case PausePropertyId when property.Format == LibMpvNative.MPV_FORMAT_FLAG:
                if (_propertyCache.TryUpdatePause(Marshal.ReadInt32(property.Data) != 0))
                    PublishPlaybackState(sourceId);
                break;

            case SeekablePropertyId when property.Format == LibMpvNative.MPV_FORMAT_FLAG:
                if (_propertyCache.TryUpdateSeekable(Marshal.ReadInt32(property.Data) != 0))
                    PublishPlaybackState(sourceId);
                break;

            case SubDelayPropertyId when property.Format == LibMpvNative.MPV_FORMAT_DOUBLE:
                var delay = *(double*)property.Data;
                SubtitleDelayChanged?.Invoke(delay);
                SourceEvent?.Invoke(new MpvSourceEvent.SubtitleDelay(sourceId, delay));
                break;

            case EofReachedPropertyId when property.Format == LibMpvNative.MPV_FORMAT_FLAG:
                var eof = Marshal.ReadInt32(property.Data) != 0;
                EofReachedChanged?.Invoke(eof);
                var state = _propertyCache.PlaybackState;
                if (eof)
                {
                    state = Read(handle => state with
                    {
                        Position = GetPlaybackDouble(handle, "time-pos", state.Position),
                        Duration = GetPlaybackDouble(handle, "duration", state.Duration)
                    }, state);
                }
                SourceEvent?.Invoke(new MpvSourceEvent.Eof(sourceId, eof, state));
                break;

            case PausedForCachePropertyId when property.Format == LibMpvNative.MPV_FORMAT_FLAG:
                var buffering = Marshal.ReadInt32(property.Data) != 0;
                BufferingChanged?.Invoke(buffering);
                SourceEvent?.Invoke(new MpvSourceEvent.Buffering(sourceId, buffering));
                break;
        }
    }

    private void PublishPlaybackState(long sourceId)
    {
        var state = _propertyCache.PlaybackState;
        PlaybackStateChanged?.Invoke(state);
        SourceEvent?.Invoke(new MpvSourceEvent.State(sourceId, state));
    }
}
