namespace Kiriha.Mpv;

public sealed class MpvPlaybackEndedEventArgs : EventArgs
{
    public const int ReasonEof = 0;
    public const int ReasonQuit = 3;
    public const int ReasonError = 4;
    internal const int ReasonRedirect = 5;

    public MpvPlaybackEndedEventArgs(int reason, int error)
    {
        Reason = reason;
        Error = error;
    }

    public int Reason { get; }
    public int Error { get; }
    public bool HasError => Reason == ReasonError || Error < 0;
    public bool StopsPlayback => Reason is ReasonEof or ReasonQuit or ReasonError;
    public string? ErrorMessage => Error < 0 ? LibMpvNative.GetErrorString(Error) : null;
}
