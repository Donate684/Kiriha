using System;
using System.Runtime.InteropServices;

namespace Kiriha.Mpv;

public partial class MpvPlayer
{
    internal static void Check(int result, string action)
    {
        if (result < 0)
            throw new InvalidOperationException($"mpv failed to {action}: {LibMpvNative.GetErrorString(result)}");
    }

    internal static string? GetPropertyString(IntPtr handle, string name)
    {
        IntPtr ptr = LibMpvNative.mpv_get_property_string(handle, name);
        if (ptr == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            LibMpvNative.mpv_free(ptr);
        }
    }

    private static MpvRuntimeDiagnostics FormatRuntimeVideoInfo(
        string? hwdec,
        string? interop,
        string? vo,
        string? gpuContext,
        string? decoder,
        string? vcodec,
        string? width,
        string? height,
        string? fps,
        string? dropped,
        string? voDropped)
    {
        string res = (string.IsNullOrWhiteSpace(width) || width == "-") ? "" : $"{width}x{height}";
        if (!string.IsNullOrWhiteSpace(fps) && fps != "-")
            res += string.IsNullOrEmpty(res) ? $"{fps} fps" : $" @ {fps} fps";

        string codec = (string.IsNullOrWhiteSpace(vcodec) || vcodec == "-") ? "" : $"Codec: {vcodec}";
        string dec = (string.IsNullOrWhiteSpace(decoder) || decoder == "-") ? "" : $"Decoder: {decoder}";
        string codecDec = string.Join(" | ", new[] { codec, dec }.Where(s => !string.IsNullOrEmpty(s)));

        bool isHwDecActive = !string.IsNullOrWhiteSpace(hwdec) && hwdec != "no" && hwdec != "-";
        
        string hw = isHwDecActive ? $"HWDec: {hwdec}{(string.IsNullOrWhiteSpace(interop) || interop == "-" ? "" : $" ({interop})")}" : "HWDec: off";
        string vout = (string.IsNullOrWhiteSpace(vo) || vo == "-") ? "" : $"VO: {vo}{(string.IsNullOrWhiteSpace(gpuContext) || gpuContext == "-" ? "" : $" ({gpuContext})")}";
        string hwVo = string.Join(" | ", new[] { hw, vout }.Where(s => !string.IsNullOrEmpty(s)));

        string dropStr = (string.IsNullOrWhiteSpace(dropped) || dropped == "-" || dropped == "0") ? "" : $"{dropped} (Dec)";
        string voDropStr = (string.IsNullOrWhiteSpace(voDropped) || voDropped == "-" || voDropped == "0") ? "" : $"{voDropped} (VO)";
        string drops = string.Join(" / ", new[] { dropStr, voDropStr }.Where(s => !string.IsNullOrEmpty(s)));
        if (!string.IsNullOrEmpty(drops)) drops = "Dropped: " + drops;

        return new MpvRuntimeDiagnostics
        {
            ResolutionFps = res,
            CodecDecoder = codecDec,
            HwDecVo = hwVo,
            Drops = drops,
            IsHwDecActive = isHwDecActive
        };
    }

    internal static void SetMpvOption(IntPtr handle, string name, string value, string action)
    {
        Check(LibMpvNative.mpv_set_property_string(handle, name, value), action);
    }

    internal static string FormatDouble(double value)
    {
        return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
