namespace Kiriha.Mpv;

public sealed record MpvOptions(
    string Hwdec,
    string VideoOutput,
    string GpuApi,
    string GpuContext,
    string VideoSync = "no",
    bool Interpolation = false,
    string TemporalScale = "oversample")
{
    public static MpvOptions Default { get; } = new("auto", "gpu-next", "auto", "auto");
}

