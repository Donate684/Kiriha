using Kiriha.Core.Abstractions.Models.Entities;
using Kiriha.Services.Data.Image;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Kiriha.Core.Infrastructure;

namespace Kiriha.Services.Data.Image;

public class PosterBatchDownloader
{
    private readonly ImageCacheService _imageCache;
    private readonly IUiDispatcher _uiDispatcher;

    public PosterBatchDownloader(ImageCacheService imageCache, IUiDispatcher uiDispatcher)
    {
        _imageCache = imageCache;
        _uiDispatcher = uiDispatcher;
    }

    public async Task CacheBatchAsync(IEnumerable<AnimeEntity> items, Action<int, int>? onProgress = null, CancellationToken ct = default)
    {
        var toDownload = items.Where(NeedsPosterDownload).ToList();

        if (toDownload.Count == 0) return;

        int count = 0;
        if (toDownload.Count == 1)
        {
            await CachePosterAsync(toDownload[0], toDownload.Count, onProgress, () => Interlocked.Increment(ref count), ct);
            return;
        }

        var tasks = toDownload.Select(async item =>
        {
            await CachePosterAsync(item, toDownload.Count, onProgress, () => Interlocked.Increment(ref count), ct);
        });

        await Task.WhenAll(tasks);
    }

    private static bool NeedsPosterDownload(AnimeEntity item)
    {
        if (string.IsNullOrEmpty(item.MainPictureUrl))
            return false;

        if (string.IsNullOrEmpty(item.LocalPosterPath) || !File.Exists(item.LocalPosterPath))
            return true;

        return new FileInfo(item.LocalPosterPath).Length == 0;
    }

    private async Task CachePosterAsync(
        AnimeEntity item,
        int total,
        Action<int, int>? onProgress,
        Func<int> increment,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var localPath = await _imageCache.GetLocalPathOrDownload(item.MainPictureUrl!, ct);
            if (string.IsNullOrEmpty(localPath) || ct.IsCancellationRequested)
                return;

            _uiDispatcher.Post(() =>
            {
                if (ct.IsCancellationRequested) return;
                item.LocalPosterPath = localPath;
            });
            onProgress?.Invoke(increment(), total);
        }
        catch (OperationCanceledException) { }
    }
}
