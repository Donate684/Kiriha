using System;
using System.Linq;

namespace Kiriha.Mpv;

public sealed partial class MpvThumbnailer
{
    private bool TryEnterActiveCall(out IntPtr handle)
    {
        lock (_gate)
        {
            if (_disposed || _handle == IntPtr.Zero)
            {
                handle = IntPtr.Zero;
                return false;
            }
            _activeCalls++;
            handle = _handle;
            return true;
        }
    }

    private void LeaveActiveCall()
    {
        lock (_gate)
        {
            _activeCalls--;
            if (_disposed && _activeCalls == 0 && _handle != IntPtr.Zero)
            {
                LibMpvNative.mpv_terminate_destroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }

    public static int GetCacheBucket(double timeSeconds) => ToBucket(timeSeconds);

    private void TrimCache()
    {
        if (_cache.Count <= MaxCacheItems)
            return;

        foreach (var pair in _cache.OrderBy(x => x.Value.LastUsedUtc).Take(_cache.Count - MaxCacheItems).ToArray())
        {
            _cache.Remove(pair.Key);
        }
    }

    private static int ToBucket(double timeSeconds)
    {
        return Math.Max(0, (int)Math.Round(timeSeconds / CacheStepSeconds));
    }
}
