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

    private static string FormatRuntimeVideoInfo(
        string? hwdec,
        string? interop,
        string? vo,
        string? gpuContext,
        string? decoder)
    {
        static string ValueOrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

        return $"hwdec: {ValueOrDash(hwdec)}, interop: {ValueOrDash(interop)}, vo: {ValueOrDash(vo)}, context: {ValueOrDash(gpuContext)}, decoder: {ValueOrDash(decoder)}";
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
