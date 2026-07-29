using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Core.Mpv;

public sealed partial class MpvThumbnailer : IDisposable
{
    private const int MaxCacheItems = 80;
    private const double CacheStepSeconds = 2.0;
    private const int ThumbnailWidth = 640;
    private const int ThumbnailHeight = 360;

    private readonly object _gate = new();
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly string _thumbnailDirectory;
    private readonly FileStream _lockFile;
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
        _thumbnailDirectory = Path.Combine(Path.GetTempPath(), "Kiriha", "timeline-thumbs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_thumbnailDirectory);
        _lockFile = new FileStream(Path.Combine(_thumbnailDirectory, ".lock"), FileMode.Create, FileAccess.Write, FileShare.Read);

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
        SetOption("vd-lavc-threads", "2");
        SetOption("sws-scaler", "bicubic");
        SetOption("vf", $"lavfi=[scale=w={ThumbnailWidth}:h={ThumbnailHeight}:force_original_aspect_ratio=decrease:flags=bicubic,pad=w={ThumbnailWidth}:h={ThumbnailHeight}:x=(ow-iw)/2:y=(oh-ih)/2]");
        SetOption("demuxer-max-bytes", "16MiB");
        SetOption("demuxer-max-back-bytes", "4MiB");
        SetOption("screenshot-format", "jpg");
        SetOption("screenshot-jpeg-quality", "90");

        Check(LibMpvNative.mpv_initialize(_handle), "initialize mpv thumbnailer");
    }

    public async Task<string?> GetThumbnailAsync(string videoPath, double timeSeconds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return null;

        var bucket = ToBucket(timeSeconds);
        lock (_gate)
        {
            if (_cache.TryGetValue(bucket, out var cached) && File.Exists(cached.Path))
            {
                cached.LastUsedUtc = DateTime.UtcNow;
                return cached.Path;
            }
        }

        await _captureGate.WaitAsync(cancellationToken);
        try
        {
            lock (_gate)
            {
                if (_cache.TryGetValue(bucket, out var cached) && File.Exists(cached.Path))
                {
                    cached.LastUsedUtc = DateTime.UtcNow;
                    return cached.Path;
                }
            }

            return await Task.Run(() => CaptureThumbnail(videoPath, bucket, cancellationToken), cancellationToken);
        }
        finally
        {
            _captureGate.Release();
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
            catch (OperationCanceledException)
            {
                // ignore
            }
            finally
            {
                _captureGate.Release();
            }
        }, cancellationToken);
    }



    ~MpvThumbnailer()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
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
                if (_handle != IntPtr.Zero)
                {
                    LibMpvNative.mpv_wakeup(_handle);
                    if (_activeCalls == 0)
                    {
                        LibMpvNative.mpv_terminate_destroy(_handle);
                        _handle = IntPtr.Zero;
                    }
                }
            }

            try
            {
                _lockFile?.Dispose();
            }
            catch (Exception ex) { Log.Debug(ex, "Failed to dispose lock file"); }
        }

        try
        {
            if (Directory.Exists(_thumbnailDirectory))
                Directory.Delete(_thumbnailDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to delete thumbnail directory during dispose");
            // Temp cleanup is allowed to fail when an image is still being released by UI.
        }
    }


}
