using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Serilog;

namespace Kiriha.Mpv;

public sealed partial class MpvThumbnailer
{
    private MpvThumbnailFrame? CaptureThumbnail(string videoPath, int bucket, CancellationToken cancellationToken)
    {
        if (!TryEnterActiveCall(out var handle))
            return null;

        try
        {
            EnsureLoaded(handle, videoPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var seconds = (bucket * CacheStepSeconds).ToString("0.###", CultureInfo.InvariantCulture);
            Check(LibMpvNative.mpv_command_string(handle, "seek", seconds, "absolute+keyframes"), "seek thumbnailer");

            if (cancellationToken.WaitHandle.WaitOne(45))
                cancellationToken.ThrowIfCancellationRequested();

            if (!TryCaptureRawScreenshot(handle, out var frame) || frame == null)
                return null;

            lock (_gate)
            {
                _cache[bucket] = new MpvThumbnailCacheEntry(frame);
                TrimCache();
            }
            return frame;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to generate timeline thumbnail in memory");
            return null;
        }
        finally
        {
            LeaveActiveCall();
        }
    }

    private static unsafe bool TryCaptureRawScreenshot(IntPtr handle, out MpvThumbnailFrame? frame)
    {
        frame = null;

        var cmdStr = Marshal.StringToCoTaskMemUTF8("screenshot-raw");
        var argStr = Marshal.StringToCoTaskMemUTF8("video");

        try
        {
            var nodes = stackalloc MpvNode[2];
            nodes[0].Format = LibMpvNative.MPV_FORMAT_STRING;
            nodes[0].U.String = cmdStr;
            nodes[1].Format = LibMpvNative.MPV_FORMAT_STRING;
            nodes[1].U.String = argStr;

            var list = new MpvNodeList(2, (IntPtr)nodes, IntPtr.Zero);
            var cmdNode = new MpvNode
            {
                Format = LibMpvNative.MPV_FORMAT_NODE_ARRAY,
                U = new MpvNodeUnion { List = (IntPtr)(&list) }
            };

            int res = LibMpvNative.mpv_command_node(handle, ref cmdNode, out var result);
            if (res < 0)
            {
                return false;
            }

            try
            {
                if (result.Format != LibMpvNative.MPV_FORMAT_NODE_MAP || result.U.List == IntPtr.Zero)
                    return false;

                var resList = *(MpvNodeList*)result.U.List;
                if (resList.Num <= 0 || resList.Keys == IntPtr.Zero || resList.Values == IntPtr.Zero)
                    return false;

                int width = 0;
                int height = 0;
                int stride = 0;
                IntPtr pData = IntPtr.Zero;
                int dataSize = 0;

                IntPtr* pKeys = (IntPtr*)resList.Keys;
                MpvNode* pValues = (MpvNode*)resList.Values;

                for (int i = 0; i < resList.Num; i++)
                {
                    if (pKeys[i] == IntPtr.Zero) continue;
                    var key = Marshal.PtrToStringUTF8(pKeys[i]);

                    if (key == "w" && pValues[i].Format == LibMpvNative.MPV_FORMAT_INT64)
                        width = (int)pValues[i].U.Int64;
                    else if (key == "h" && pValues[i].Format == LibMpvNative.MPV_FORMAT_INT64)
                        height = (int)pValues[i].U.Int64;
                    else if (key == "stride" && pValues[i].Format == LibMpvNative.MPV_FORMAT_INT64)
                        stride = (int)pValues[i].U.Int64;
                    else if (key == "data" && pValues[i].Format == LibMpvNative.MPV_FORMAT_BYTE_ARRAY && pValues[i].U.ByteArray != IntPtr.Zero)
                    {
                        // mpv_byte_array: void *data at offset 0, size_t size at offset sizeof(void*)
                        pData = Marshal.ReadIntPtr(pValues[i].U.ByteArray);
                        dataSize = (int)Marshal.ReadInt64(pValues[i].U.ByteArray, IntPtr.Size);
                    }
                }

                if (width > 0 && height > 0 && pData != IntPtr.Zero && dataSize > 0)
                {
                    var bgraPixels = GC.AllocateUninitializedArray<byte>(dataSize);
                    Marshal.Copy(pData, bgraPixels, 0, dataSize);

                    // Ensure 100% opacity (Alpha = 255) for all 32-bit BGR0/BGRA pixels
                    fixed (byte* p = bgraPixels)
                    {
                        uint* pUint = (uint*)p;
                        int pixelCount = dataSize / 4;
                        for (int i = 0; i < pixelCount; i++)
                        {
                            pUint[i] |= 0xFF000000;
                        }
                    }

                    frame = new MpvThumbnailFrame(width, height, stride > 0 ? stride : width * 4, bgraPixels);
                    return true;
                }

                return false;
            }
            finally
            {
                LibMpvNative.mpv_free_node_contents(ref result);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to capture in-memory screenshot from mpv");
            return false;
        }
        finally
        {
            if (cmdStr != IntPtr.Zero) Marshal.FreeCoTaskMem(cmdStr);
            if (argStr != IntPtr.Zero) Marshal.FreeCoTaskMem(argStr);
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
            var eventPtr = LibMpvNative.mpv_wait_event(handle, 0.1);
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
                    MpvEventEndFile endFile = default;
                    if (mpvEvent.Data != IntPtr.Zero)
                    {
                        unsafe { endFile = *(MpvEventEndFile*)mpvEvent.Data; }
                    }
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
