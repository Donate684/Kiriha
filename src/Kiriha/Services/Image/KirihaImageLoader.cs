using System;
using System.Threading;
using System.Threading.Tasks;
using AsyncImageLoader;
using AsyncImageLoader.Core.Leases;
using AsyncImageLoader.Core.Pipeline;
using Avalonia.Media.Imaging;
using Kiriha.Services.Data.Image;
using Serilog;

namespace Kiriha.Services.Data.Image;

public class KirihaImageLoader : IAsyncImageLoader
{
    private readonly ImageCacheService _imageCache;

    public KirihaImageLoader(ImageCacheService imageCache)
    {
        _imageCache = imageCache;
    }

    public async Task<IImageLease?> LoadAsync(ImageLoadRequest request, CancellationToken cancellationToken)
    {
        var url = request.Source?.ToString();
        if (string.IsNullOrEmpty(url)) return null;

        try
        {
            var bitmap = await _imageCache.LoadBitmapAsync(url);
            if (bitmap == null) return null;
            return ImageLease.NonOwning(bitmap);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ImageLoad] Failed to load {Url}", url);
            return null;
        }
    }

    public void Dispose()
    {
        // Disposal of ImageCacheService is handled by DI container.
    }
}
