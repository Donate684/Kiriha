namespace Kiriha.Mpv;

public readonly record struct PlaybackState(
    double Position,
    double Duration,
    bool IsPlaying,
    bool IsSeekable,
    bool IsLoaded);
