using Kiriha.Services.Data.Image;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Kiriha.Core.Infrastructure;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Kiriha.Core.Services.AppLifecycle;
using Kiriha.Services.AppLifecycle;
using Serilog;

namespace Kiriha.Services.Data.Image;

public class ImageCacheService : IDisposable
{
    private readonly string CacheRoot = Kiriha.Core.Platform.PathHelper.GetImageCachePath();

    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ImageDownloader _downloader;
    private readonly ImageDiskCache _diskCache;
    private readonly ImageCacheCleanup _cleanup;

    private readonly SemaphoreSlim _decodeSemaphore = new(12, 12);

    private readonly BitmapMemoryCache _memCache = new(
        encodedBudgetBytes: 32L * 1024 * 1024,
        pixelBudgetBytes: 16L * 1024 * 1024);

    public ImageCacheService(
        IHttpClientFactory httpClientFactory,
        IBackgroundTaskSupervisor backgroundTasks,
        IUiDispatcher uiDispatcher)
    {
        _backgroundTasks = backgroundTasks;
        _uiDispatcher = uiDispatcher;
        _downloader = new ImageDownloader(httpClientFactory, CacheRoot);
        _diskCache = new ImageDiskCache(CacheRoot, _downloader);
        _cleanup = new ImageCacheCleanup(CacheRoot);

        _cleanup.ScheduleStartupCleanup(_backgroundTasks);
    }

    public async Task<Bitmap?> LoadBitmapAsync(string url, int decodeWidth = 300, CancellationToken ct = default)
    {
        string localPath = await _diskCache.ResolveLocalPathAsync(url, ct);

        if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath)) return null;

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
                    if (!_memCache.TryGetEncoded(localPath, out var bytes) || bytes == null)
                    {
                        bytes = File.ReadAllBytes(localPath);
                        _memCache.StoreEncoded(localPath, bytes);
                    }

                    using var ms = new MemoryStream(bytes, writable: false);
                    var bmp = Bitmap.DecodeToWidth(ms, decodeWidth);

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

    public void ClearMemoryCache() => _memCache.Clear();

    public Task<string> GetLocalPathOrDownload(string url, CancellationToken ct = default)
    {
        return _downloader.GetLocalPathOrDownload(url, ct);
    }

    public void Dispose()
    {
        _downloader.Dispose();
        _decodeSemaphore.Dispose();
        _memCache.Clear();
    }
}
