namespace Kiriha.Mpv;

public readonly struct MpvRuntimeDiagnostics
{
    public string ResolutionFps { get; init; }
    public string CodecDecoder { get; init; }
    public string HwDecVo { get; init; }
    public string Drops { get; init; }
    public bool IsHwDecActive { get; init; }
}
