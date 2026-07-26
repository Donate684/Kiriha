using System;
using Serilog;

namespace Kiriha.Core.Mpv;

public partial class MpvPlayer
{
    internal T Read<T>(Func<IntPtr, T> read, T defaultValue)
    {
        IntPtr handle;
        lock (_gate)
        {
            if (_disposed || _mpvHandle == IntPtr.Zero) return defaultValue;
            handle = _mpvHandle;
        }

        return read(handle);
    }

    internal T ReadNodeProperty<T>(string name, Func<MpvNode, T> parse, T defaultValue)
    {
        return Read(handle =>
        {
            int result = LibMpvNative.mpv_get_property_node(handle, name, LibMpvNative.MPV_FORMAT_NODE, out var node);
            if (result < 0)
                return defaultValue;

            try
            {
                return parse(node);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to parse mpv node property {PropertyName}", name);
                return defaultValue;
            }
            finally
            {
                LibMpvNative.mpv_free_node_contents(ref node);
            }
        }, defaultValue);
    }

    internal double ReadDoubleProperty(string name, double defaultValue)
    {
        return Read(handle =>
        {
            var result = LibMpvNative.mpv_get_property(handle, name, LibMpvNative.MPV_FORMAT_DOUBLE, out var value);
            return result < 0 ? defaultValue : value;
        }, defaultValue);
    }
}
