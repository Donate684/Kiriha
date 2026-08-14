using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Serilog;

namespace Kiriha.Mpv;

public sealed partial class MpvThumbnailer
{
    private string? CaptureThumbnail(string videoPath, int bucket, CancellationToken cancellationToken)
    {
        if (!TryEnterActiveCall(out var handle))
            return null;

        try
        {
            EnsureLoaded(handle, videoPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var targetPath = Path.Combine(_thumbnailDirectory, $"thumb-{bucket:000000}.jpg");
            TryDelete(targetPath);

            var seconds = (bucket * CacheStepSeconds).ToString("0.###", CultureInfo.InvariantCulture);
            Check(LibMpvNative.mpv_command_string(handle, "seek", seconds, "absolute+keyframes"), "seek thumbnailer");

            if (cancellationToken.WaitHandle.WaitOne(45))
                cancellationToken.ThrowIfCancellationRequested();

            Check(LibMpvNative.mpv_command_string(handle, "screenshot-to-file", targetPath, "video"), "capture thumbnail");
            if (!WaitForFile(targetPath, cancellationToken))
                return null;

            lock (_gate)
            {
                _cache[bucket] = new MpvThumbnailCacheEntry(targetPath);
                TrimCache();
            }
            return targetPath;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to generate timeline thumbnail");
            return null;
        }
        finally
        {
            LeaveActiveCall();
        }
    }

    private void EnsureLoaded(IntPtr handle, string videoPath, CancellationToken cancellationToken)
    {
        bool needsLoad = false;
        lock (_gate)
        {
            if (!string.Equals(_loadedPath, videoPath, StringComparison.Ordinal))
            {
                _cache.Clear();
                _loadedPath = videoPath;
                needsLoad = true;
            }
        }

        if (!needsLoad)
            return;

        Check(LibMpvNative.mpv_command_string(handle, "loadfile", videoPath, "replace"), "load thumbnail source");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool loaded = false;
        while (sw.ElapsedMilliseconds < 3000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var eventPtr = LibMpvNative.mpv_wait_event(handle, 0.01);
            if (eventPtr != IntPtr.Zero)
            {
                var mpvEvent = Marshal.PtrToStructure<MpvEvent>(eventPtr);
                if (mpvEvent.EventId == LibMpvNative.MPV_EVENT_FILE_LOADED)
                {
                    loaded = true;
                    break;
                }

                if (mpvEvent.EventId == LibMpvNative.MPV_EVENT_END_FILE)
                {
                    var endFile = mpvEvent.Data == IntPtr.Zero
                        ? new MpvEventEndFile()
                        : Marshal.PtrToStructure<MpvEventEndFile>(mpvEvent.Data);
                    if (endFile.Reason == MpvPlaybackEndedEventArgs.ReasonError)
                    {
                        Log.Warning("Thumbnailer failed to load: {VideoPath}", videoPath);
                        break;
                    }
                }
            }
        }

        if (!loaded)
        {
            lock (_gate)
            {
                if (string.Equals(_loadedPath, videoPath, StringComparison.Ordinal))
                    _loadedPath = null;
            }
            throw new InvalidOperationException($"Thumbnailer failed to load file within timeout: {videoPath}");
        }
    }
}
