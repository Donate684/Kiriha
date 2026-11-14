namespace Kiriha.Mpv;

public sealed record MpvOptions(
    string Hwdec,
    string VideoOutput,
    string GpuApi,
    string GpuContext,
    string VideoSync = "no",
    bool Interpolation = false,
    string TemporalScale = "oversample",
    bool SavePositionOnQuit = true,
    string? WatchLaterDirectory = null)
{
    public static MpvOptions Default { get; } = new("auto", "libmpv", "auto", "auto");

    public static MpvOptions Sanitize(MpvOptions options)
    {
        var gpuApi = options.GpuApi?.Trim().ToLowerInvariant();
        if (gpuApi is "d3d11" or "vulkan" or "" or null)
        {
            gpuApi = "auto";
        }
        else if (gpuApi is not ("auto" or "opengl"))
        {
            gpuApi = "auto";
        }

        return options with
        {
            VideoOutput = "libmpv",
            GpuApi = gpuApi,
            GpuContext = "auto"
        };
    }
}

