namespace Kiriha.Mpv;

/// <summary>Notifications scoped to a single load request, including reloads of the same URL.</summary>
public abstract record MpvSourceEvent(long SourceId)
{
    public sealed record Loaded(long SourceId, PlaybackState Playback) : MpvSourceEvent(SourceId);
    public sealed record Ended(long SourceId, MpvPlaybackEndedEventArgs Details, PlaybackState Playback) : MpvSourceEvent(SourceId);
    public sealed record State(long SourceId, PlaybackState Playback) : MpvSourceEvent(SourceId);
    public sealed record Tracks(long SourceId) : MpvSourceEvent(SourceId);
    public sealed record SubtitleDelay(long SourceId, double Seconds) : MpvSourceEvent(SourceId);
    public sealed record Eof(long SourceId, bool Reached, PlaybackState Playback) : MpvSourceEvent(SourceId);
    public sealed record Buffering(long SourceId, bool Active) : MpvSourceEvent(SourceId);
}
