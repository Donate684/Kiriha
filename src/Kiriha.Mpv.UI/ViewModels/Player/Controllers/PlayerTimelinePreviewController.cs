using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Kiriha.Mpv.UI.ViewModels.Player;

public sealed class PlayerTimelinePreviewController : IDisposable
{
    private readonly PlayerOverlayViewModel _overlay;
    private readonly Func<bool>? _isTorrentStream;
    private readonly Func<IReadOnlyDictionary<string, string>?>? _getStreamHeaders;
    private MpvThumbnailer? _thumbnailer;
    private CancellationTokenSource? _thumbnailCts;
    private CancellationTokenSource? _warmUpCts;
    private int _requestId;
    private int _previewBucket = -1;
    private int _displayedBucket = -1;

    public PlayerTimelinePreviewController(
        PlayerOverlayViewModel overlay,
        Func<bool>? isTorrentStream = null,
        Func<IReadOnlyDictionary<string, string>?>? getStreamHeaders = null)
    {
        _overlay = overlay;
        _isTorrentStream = isTorrentStream;
        _getStreamHeaders = getStreamHeaders;
    }

    public void Initialize()
    {
        // Thumbnailer is initialized lazily on first hover
    }

    public void Show(string videoUrl, double duration, double timeSeconds, double left, string? segmentLabel = null)
    {
        if (duration <= 0 || string.IsNullOrWhiteSpace(videoUrl))
        {
            Hide();
            return;
        }

        try
        {
            _overlay.ShowTimelinePreview(timeSeconds, left, segmentLabel);

            // Never spin up a second libmpv instance against TorrServer's 0-disk RAM preload stream:
            // concurrent random seeks evict pieces ahead of the active playback cursor and stall video.
            if (_isTorrentStream?.Invoke() == true)
            {
                _overlay.SetTimelinePreviewImage(null);
                return;
            }

            var headers = _getStreamHeaders?.Invoke();

            if (_thumbnailer is null)
            {
                _thumbnailer = CreateThumbnailer();
                if (_thumbnailer != null)
                    _ = _thumbnailer.WarmUpAsync(videoUrl, default, headers);
            }

            var thumbnailer = _thumbnailer;
            if (thumbnailer is null)
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

            _ = ShowThumbnailAsync(thumbnailer, videoUrl, timeSeconds, bucket, requestId, headers, token);
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Failed to initiate timeline preview");
        }
    }

    private async Task ShowThumbnailAsync(
        MpvThumbnailer thumbnailer,
        string videoUrl,
        double timeSeconds,
        int bucket,
        int requestId,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken token)
    {
        try
        {
            // Debounce rapid scrub gestures so intermediate transient frames are not processed
            await Task.Delay(50, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || requestId != _requestId)
                return;

            var frame = await thumbnailer.GetThumbnailAsync(videoUrl, timeSeconds, token, headers);
            if (token.IsCancellationRequested || requestId != _requestId || frame is null)
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
        _overlay.ClearTimelinePreview();
        if (_isTorrentStream?.Invoke() == true)
            return;

        var thumbnailer = _thumbnailer;
        if (thumbnailer is null || string.IsNullOrWhiteSpace(videoUrl))
            return;

        var headers = _getStreamHeaders?.Invoke();

        _warmUpCts?.Cancel();
        _warmUpCts?.Dispose();
        _warmUpCts = new CancellationTokenSource();
        var token = _warmUpCts.Token;

        _ = Task.Run(() =>
        {
            if (token.IsCancellationRequested || !IsValidVideoSource(videoUrl))
                return Task.CompletedTask;

            return thumbnailer.WarmUpAsync(videoUrl, token, headers)
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

    private static bool IsValidVideoSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (File.Exists(path)) return true;
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
