using System;
using System.IO;
using System.Linq;
using System.Threading;
using Serilog;

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
            TryDelete(pair.Value.Path);
        }
    }

    private static int ToBucket(double timeSeconds)
    {
        return Math.Max(0, (int)Math.Round(timeSeconds / CacheStepSeconds));
    }

    private static bool WaitForFile(string path, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 20; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                try { if (new FileInfo(path).Length > 0) return true; }
                catch { /* race condition, retry */ }
            }

            if (cancellationToken.WaitHandle.WaitOne(25))
                cancellationToken.ThrowIfCancellationRequested();
        }

        return false;
    }

    private void SetOption(string name, string value)
    {
        Check(LibMpvNative.mpv_set_option_string(_handle, name, value), $"set thumbnailer {name}");
    }

    private static void Check(int result, string action)
    {
        if (result < 0)
            throw new InvalidOperationException($"Failed to {action}: {LibMpvNative.GetErrorString(result)}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}
