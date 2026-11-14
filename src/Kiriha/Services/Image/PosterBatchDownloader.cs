using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Services.Data.Image;

public class PosterBatchDownloader
{
    private readonly IImageCacheService _imageCache;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IUserAnimeRepository? _userAnimeRepo;

    public PosterBatchDownloader(
        IImageCacheService imageCache,
        IUiDispatcher uiDispatcher,
        IUserAnimeRepository? userAnimeRepo = null)
    {
        _imageCache = imageCache;
        _uiDispatcher = uiDispatcher;
        _userAnimeRepo = userAnimeRepo;
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

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(toDownload, options, async (item, token) =>
        {
            await CachePosterAsync(item, toDownload.Count, onProgress, () => Interlocked.Increment(ref count), token);
        });
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

            _uiDispatcher.Post(async () =>
            {
                if (ct.IsCancellationRequested) return;
                item.LocalPosterPath = localPath;
                if (_userAnimeRepo != null)
                {
                    try
                    {
                        await _userAnimeRepo.UpdateMetadataAsync(item);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "PosterBatchDownloader: failed to persist local poster path for {Id}", item.Id);
                    }
                }
            });
            onProgress?.Invoke(increment(), total);
        }
        catch (OperationCanceledException) { }
    }
}
