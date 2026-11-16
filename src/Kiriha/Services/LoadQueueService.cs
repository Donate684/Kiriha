using System.Threading.Channels;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Image;
using Kiriha.Services.Data.Metadata;
using Serilog;

namespace Kiriha.Services.Data.Core;

public class LoadQueueService : ILoadQueueService, IDisposable
{
    private readonly PosterBatchDownloader _posterBatchDownloader;
    private readonly IShikiMetadataService _shikiMetadata;
    private readonly ISettingsService _settings;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;

    private const int ImageWorkerCount = 5;
    private const int ShikiWorkerCount = 2;
    private const int ImageQueueCapacity = 512;
    private const int ShikiQueueCapacity = 256;

    private readonly Lock _dedupeLock = new();
    private readonly HashSet<MediaKey> _queuedForImage = new();
    private readonly HashSet<MediaKey> _queuedForShiki = new();
    private readonly Channel<AnimeEntity> _imageQueue = CreateQueue(ImageQueueCapacity);
    private readonly Channel<AnimeEntity> _shikiQueue = CreateQueue(ShikiQueueCapacity);

    public LoadQueueService(
        PosterBatchDownloader posterBatchDownloader,
        IShikiMetadataService shikiMetadata,
        ISettingsService settings,
        IBackgroundTaskSupervisor backgroundTasks)
    {
        _posterBatchDownloader = posterBatchDownloader;
        _shikiMetadata = shikiMetadata;
        _settings = settings;
        _backgroundTasks = backgroundTasks;

        for (int i = 0; i < ImageWorkerCount; i++)
        {
            _backgroundTasks.Run($"LoadQueueService.ImageWorker.{i + 1}", ImageWorkerLoopAsync);
        }

        for (int i = 0; i < ShikiWorkerCount; i++)
        {
            _backgroundTasks.Run($"LoadQueueService.ShikiWorker.{i + 1}", ShikiWorkerLoopAsync);
        }
    }

    public void EnqueueForViewport(IEnumerable<AnimeEntity> items)
    {
        foreach (var item in items)
        {
            if (item is null) continue;

            bool needsImage = !string.IsNullOrEmpty(item.MainPictureUrl);
            bool needsShiki = (_settings.Current.UI.UseRussianTitles && string.IsNullOrEmpty(item.RussianTitle)) ||
                (_settings.Current.UI.UseRussianDescriptions && string.IsNullOrEmpty(item.RussianSynopsis));

            if (needsImage)
                EnqueueItem(item, _imageQueue.Writer, _queuedForImage);

            if (needsShiki)
                EnqueueItem(item, _shikiQueue.Writer, _queuedForShiki);
        }
    }

    private static Channel<AnimeEntity> CreateQueue(int capacity)
    {
        return Channel.CreateBounded<AnimeEntity>(new BoundedChannelOptions(capacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    private void EnqueueItem(AnimeEntity item, ChannelWriter<AnimeEntity> writer, HashSet<MediaKey> queuedKeys)
    {
        lock (_dedupeLock)
        {
            if (!queuedKeys.Add(item.Key))
                return;
        }

        if (writer.TryWrite(item))
            return;

        _ = EnqueueSlowAsync(item, writer, queuedKeys);
    }

    private async Task EnqueueSlowAsync(AnimeEntity item, ChannelWriter<AnimeEntity> writer, HashSet<MediaKey> queuedKeys)
    {
        try
        {
            await writer.WriteAsync(item);
        }
        catch (ChannelClosedException)
        {
            lock (_dedupeLock)
            {
                queuedKeys.Remove(item.Key);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "LoadQueueService: failed to enqueue item {Id}", item.Id);
            lock (_dedupeLock)
            {
                queuedKeys.Remove(item.Key);
            }
        }
    }

    private async Task ImageWorkerLoopAsync(CancellationToken ct)
    {
        await foreach (var item in _imageQueue.Reader.ReadAllAsync(ct))
        {
            var batch = new List<AnimeEntity> { item };
            while (batch.Count < 16 && _imageQueue.Reader.TryRead(out var nextItem))
            {
                batch.Add(nextItem);
            }

            try
            {
                await _posterBatchDownloader.CacheBatchAsync(batch, ct: ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error pre-caching image batch in LoadQueueService");
            }
            finally
            {
                foreach (var b in batch)
                {
                    RemoveQueuedKey(_queuedForImage, b.Key);
                }
            }
        }
    }

    private async Task ShikiWorkerLoopAsync(CancellationToken ct)
    {
        await foreach (var item in _shikiQueue.Reader.ReadAllAsync(ct))
        {
            var batch = new List<AnimeEntity> { item };
            while (batch.Count < 16 && _shikiQueue.Reader.TryRead(out var nextItem))
            {
                batch.Add(nextItem);
            }

            try
            {
                await _shikiMetadata.LocalizeItemsAsync(batch, null, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error processing shiki queue batch in LoadQueueService");
            }
            finally
            {
                foreach (var b in batch)
                {
                    RemoveQueuedKey(_queuedForShiki, b.Key);
                }
            }
        }
    }

    private void RemoveQueuedKey(HashSet<MediaKey> queuedKeys, MediaKey key)
    {
        lock (_dedupeLock)
        {
            queuedKeys.Remove(key);
        }
    }

    public void Dispose()
    {
        _imageQueue.Writer.TryComplete();
        _shikiQueue.Writer.TryComplete();
    }
}
