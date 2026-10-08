using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Serilog;

namespace Kiriha.Services.Data.Image;

public class ImageCacheService : IImageCacheService, IDisposable
{
    private readonly string CacheRoot = Kiriha.Infrastructure.Platform.PathHelper.GetImageCachePath();

    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly IImageUrlRewriter? _urlRewriter;
    private readonly ImageDownloader _downloader;
    private readonly ImageDiskCache _diskCache;
    private readonly ImageCacheCleanup _cleanup;

    private readonly SemaphoreSlim _decodeSemaphore = new(12, 12);
    private readonly ConcurrentDictionary<string, string> _urlToPathMap = new(StringComparer.OrdinalIgnoreCase);

    private readonly BitmapMemoryCache _memCache = new(
        encodedBudgetBytes: 48L * 1024 * 1024,
        pixelBudgetBytes: 128L * 1024 * 1024);

    public ImageCacheService(
        IHttpClientFactory httpClientFactory,
        IBackgroundTaskSupervisor backgroundTasks,
        IImageUrlRewriter? urlRewriter = null)
    {
        _backgroundTasks = backgroundTasks;
        _urlRewriter = urlRewriter;
        _downloader = new ImageDownloader(httpClientFactory, CacheRoot, urlRewriter);
        _diskCache = new ImageDiskCache(CacheRoot, _downloader);
        _cleanup = new ImageCacheCleanup(CacheRoot);

        _cleanup.ScheduleStartupCleanup(_backgroundTasks);
    }

    public async Task<Bitmap?> LoadBitmapAsync(string url, int decodeWidth = 300, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url)) return null;

        // Normalise the URL so that in-memory cache keys are consistent
        // regardless of which Shikimori host was stored in the database.
        url = _urlRewriter?.Rewrite(url) ?? url;

        // In-memory fast path: if URL was already mapped to a local file and is present in L1 memory cache,
        // return instantly without any disk checks or hashing.
        string localPath;
        if (_urlToPathMap.TryGetValue(url, out var knownPath))
        {
            if (_memCache.TryRentBitmap(knownPath, decodeWidth, out var memRented) && memRented != null)
                return memRented;
            localPath = knownPath;
        }
        else
        {
            localPath = await _diskCache.ResolveLocalPathAsync(url, ct);
            if (string.IsNullOrEmpty(localPath)) return null;
            _urlToPathMap[url] = localPath;
        }

        if (_memCache.TryRentBitmap(localPath, decodeWidth, out var rented) && rented != null)
            return rented;

        await _decodeSemaphore.WaitAsync(ct);
        try
        {
            return await Task.Run(() =>
            {
                if (_memCache.TryRentBitmap(localPath, decodeWidth, out var rented2) && rented2 != null)
                    return rented2;

                try
                {
                    Bitmap bmp;
                    if (_memCache.TryGetEncoded(localPath, out var bytes) && bytes != null)
                    {
                        using var ms = new ReadOnlyMemoryStream(bytes);
                        bmp = decodeWidth > 0 ? Bitmap.DecodeToWidth(ms, decodeWidth) : new Bitmap(ms);
                    }
                    else
                    {
                        using var fs = File.OpenRead(localPath);
                        bmp = decodeWidth > 0 ? Bitmap.DecodeToWidth(fs, decodeWidth) : new Bitmap(fs);
                    }

                    _memCache.StorePixelsFrom(localPath, decodeWidth, bmp);
                    return bmp;
                }
                catch (Exception ex)
                {
                    Log.Debug("Failed to decode bitmap {Url}: {Msg}", url, ex.Message);
                    return null;
                }
            });
        }
        finally
        {
            _decodeSemaphore.Release();
        }
    }

    public Task PerformSmartCleanupAsync(IEnumerable<string> activePaths)
    {
        return _cleanup.PerformSmartCleanupAsync(activePaths);
    }

    public void ClearMemoryCache()
    {
        _urlToPathMap.Clear();
        _memCache.Clear();
    }

    public Task<string> GetLocalPathOrDownload(string url, CancellationToken ct = default)
    {
        return _downloader.GetLocalPathOrDownload(url, ct);
    }

    public void Dispose()
    {
        _downloader.Dispose();
        _decodeSemaphore.Dispose();
        _urlToPathMap.Clear();
        _memCache.Clear();
    }
}
