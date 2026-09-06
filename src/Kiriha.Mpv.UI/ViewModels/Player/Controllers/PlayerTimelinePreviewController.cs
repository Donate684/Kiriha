using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Kiriha.Mpv;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public sealed class PlayerTimelinePreviewController : IDisposable
{
    private readonly PlayerOverlayViewModel _overlay;
    private MpvThumbnailer? _thumbnailer;
    private CancellationTokenSource? _thumbnailCts;
    private CancellationTokenSource? _warmUpCts;
    private int _requestId;
    private int _previewBucket = -1;
    private int _displayedBucket = -1;

    public PlayerTimelinePreviewController(PlayerOverlayViewModel overlay)
    {
        _overlay = overlay;
    }

    public void Initialize()
    {
        // Thumbnailer is initialized lazily on first hover
    }

    public void Show(string videoUrl, double duration, double timeSeconds, double left)
    {
        if (duration <= 0 || string.IsNullOrWhiteSpace(videoUrl))
        {
            Hide();
            return;
        }

        try
        {
            _overlay.ShowTimelinePreview(timeSeconds, left);

            if (_thumbnailer == null)
            {
                _thumbnailer = CreateThumbnailer();
                if (_thumbnailer != null)
                    _thumbnailer.WarmUpAsync(videoUrl, default);
            }

            var thumbnailer = _thumbnailer;
            if (thumbnailer == null)
                return;

            var bucket = MpvThumbnailer.GetCacheBucket(timeSeconds);
            if (bucket == _previewBucket && _overlay.TimelinePreviewImage != null)
                return;

            _previewBucket = bucket;
            var requestId = ++_requestId;
            
            var oldCts = _thumbnailCts;
            _thumbnailCts = new CancellationTokenSource();
            oldCts?.Cancel();
            oldCts?.Dispose();
            
            var token = _thumbnailCts.Token;

            _ = ShowThumbnailAsync(thumbnailer, videoUrl, timeSeconds, bucket, requestId, token);
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Failed to initiate timeline preview");
        }
    }

    private async Task ShowThumbnailAsync(MpvThumbnailer thumbnailer, string videoUrl, double timeSeconds, int bucket, int requestId, CancellationToken token)
    {
        try
        {
            // Debounce rapid scrub gestures so intermediate transient frames are not processed
            await Task.Delay(50, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || requestId != _requestId)
                return;

            var frame = await thumbnailer.GetThumbnailAsync(videoUrl, timeSeconds, token);
            if (token.IsCancellationRequested || requestId != _requestId || frame == null)
                return;

            Bitmap? bitmap = null;
            try
            {
                var pinnedHandle = System.Runtime.InteropServices.GCHandle.Alloc(frame.BgraPixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    bitmap = new Bitmap(
                        Avalonia.Platform.PixelFormat.Bgra8888,
                        Avalonia.Platform.AlphaFormat.Opaque,
                        pinnedHandle.AddrOfPinnedObject(),
                        new Avalonia.PixelSize(frame.Width, frame.Height),
                        new Avalonia.Vector(96, 96),
                        frame.Stride);
                }
                finally
                {
                    pinnedHandle.Free();
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        if (requestId != _requestId)
                            return;

                        if (_displayedBucket == bucket)
                            return;

                        _overlay.SetTimelinePreviewImage(bitmap);
                        _displayedBucket = bucket;
                        bitmap = null;
                    }
                    finally
                    {
                        bitmap?.Dispose();
                    }
                });
            }
            catch (Exception ex)
            {
                bitmap?.Dispose();
                Serilog.Log.Debug(ex, "Failed to decode timeline preview frame");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Failed to show timeline preview");
        }
    }

    public void Hide()
    {
        _requestId++;
        _previewBucket = -1;
        _displayedBucket = -1;
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = null;
        _overlay.HideTimelinePreview();
    }

    public void WarmUp(string videoUrl)
    {
        var thumbnailer = _thumbnailer;
        if (thumbnailer == null || string.IsNullOrWhiteSpace(videoUrl))
            return;

        _warmUpCts?.Cancel();
        _warmUpCts?.Dispose();
        _warmUpCts = new CancellationTokenSource();
        var token = _warmUpCts.Token;

        _ = Task.Run(() =>
        {
            if (token.IsCancellationRequested || !File.Exists(videoUrl))
                return Task.CompletedTask;

            return thumbnailer.WarmUpAsync(videoUrl, token)
                .ContinueWith(task =>
                {
                    if (task.Exception != null)
                        Serilog.Log.Debug(task.Exception, "Failed to warm up timeline thumbnailer");
                }, TaskContinuationOptions.OnlyOnFaulted);
        });
    }

    public void Dispose()
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = null;
        _warmUpCts?.Cancel();
        _warmUpCts?.Dispose();
        _warmUpCts = null;
        _displayedBucket = -1;
        _thumbnailer?.Dispose();
        _thumbnailer = null;
        _overlay.ClearTimelinePreview();
    }

    private static MpvThumbnailer? CreateThumbnailer()
    {
        try
        {
            return new MpvThumbnailer();
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Timeline thumbnailer is unavailable");
            return null;
        }
    }
}
