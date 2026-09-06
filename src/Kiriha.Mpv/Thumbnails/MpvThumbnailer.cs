using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Mpv;

public sealed partial class MpvThumbnailer : IDisposable
{
    private const int MaxCacheItems = 80;
    private const double CacheStepSeconds = 2.0;
    private const int ThumbnailWidth = 320;
    private const int ThumbnailHeight = 180;

    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly Dictionary<int, MpvThumbnailCacheEntry> _cache = new();
    private IntPtr _handle;
    private string? _loadedPath;
    private bool _disposed;
    private int _activeCalls;

    static MpvThumbnailer()
    {
        ThumbnailTempCleaner.StartCleanupTask();
    }

    public MpvThumbnailer()
    {
        _handle = LibMpvNative.mpv_create();
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create mpv thumbnailer instance.");

        SetOption("config", "no");
        SetOption("terminal", "no");
        SetOption("msg-level", "all=no");
        SetOption("idle", "yes");
        SetOption("pause", "yes");
        SetOption("keep-open", "yes");
        SetOption("osc", "no");
        SetOption("input-default-bindings", "no");
        SetOption("input-vo-keyboard", "no");
        SetOption("aid", "no");
        SetOption("sid", "no");
        SetOption("ytdl", "no");
        SetOption("hwdec", "no");
        SetOption("vo", "null");
        SetOption("force-window", "no");
        SetOption("hr-seek", "no");
        SetOption("vd-lavc-skiploopfilter", "all");
        SetOption("vd-lavc-fast", "yes");
        SetOption("vd-lavc-skipframe", "nonref");
        SetOption("vd-lavc-threads", "2");
        SetOption("vd-lavc-skipidct", "nonref");
        SetOption("video-rotate", "no");
        SetOption("sws-scaler", "bicubic");
        SetOption("demuxer-readahead-secs", "2");
        SetOption("vf", $"lavfi=[scale=w={ThumbnailWidth}:h={ThumbnailHeight}:force_original_aspect_ratio=decrease:flags=bicubic,pad=w={ThumbnailWidth}:h={ThumbnailHeight}:x=(ow-iw)/2:y=(oh-ih)/2]");
        SetOption("demuxer-max-bytes", "16MiB");
        SetOption("demuxer-max-back-bytes", "4MiB");

        Check(LibMpvNative.mpv_initialize(_handle), "initialize mpv thumbnailer");
    }

    private CancellationTokenSource? _currentCaptureCts;

    public async Task<MpvThumbnailFrame?> GetThumbnailAsync(string videoPath, double timeSeconds, CancellationToken externalCancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return null;

        var bucket = ToBucket(timeSeconds);
        lock (_gate)
        {
            if (_cache.TryGetValue(bucket, out var cached))
            {
                cached.LastUsedUtc = DateTime.UtcNow;
                return cached.Frame;
            }
        }

        CancellationTokenSource localCts;
        lock (_gate)
        {
            _currentCaptureCts?.Cancel();
            _currentCaptureCts = new CancellationTokenSource();
            localCts = _currentCaptureCts;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken, localCts.Token);
        var ct = linkedCts.Token;

        try
        {
            await _captureGate.WaitAsync(ct);
            try
            {
                lock (_gate)
                {
                    if (_cache.TryGetValue(bucket, out var cached))
                    {
                        cached.LastUsedUtc = DateTime.UtcNow;
                        return cached.Frame;
                    }
                }

                return await Task.Run(() => CaptureThumbnail(videoPath, bucket, ct), ct);
            }
            finally
            {
                _captureGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public Task WarmUpAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return Task.CompletedTask;

        return Task.Run(async () =>
        {
            await _captureGate.WaitAsync(cancellationToken);
            try
            {
                if (!TryEnterActiveCall(out var handle))
                    return;

                try
                {
                    EnsureLoaded(handle, videoPath, cancellationToken);
                }
                finally
                {
                    LeaveActiveCall();
                }
            }
            finally
            {
                _captureGate.Release();
            }
        }, cancellationToken);
    }

    private void SetOption(string name, string value)
    {
        Check(LibMpvNative.mpv_set_option_string(_handle, name, value), $"set thumbnail option {name}");
    }

    private static void Check(int error, string operation)
    {
        if (error < 0)
            throw new InvalidOperationException($"mpv thumbnailer failed to {operation}: {LibMpvNative.GetErrorString(error)}");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~MpvThumbnailer()
    {
        Dispose(false);
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _cache.Clear();
                if (_handle != IntPtr.Zero)
                {
                    LibMpvNative.mpv_wakeup(_handle);
                    if (_activeCalls == 0)
                    {
                        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
                        if (handle != IntPtr.Zero)
                            LibMpvNative.mpv_terminate_destroy(handle);
                    }
                }
            }

            try { _captureGate?.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Failed to dispose capture gate"); }
            try { _currentCaptureCts?.Cancel(); _currentCaptureCts?.Dispose(); } catch { }
        }
        else
        {
            var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    LibMpvNative.mpv_wakeup(handle);
                    LibMpvNative.mpv_terminate_destroy(handle);
                }
                catch { }
            }
        }
    }
}
