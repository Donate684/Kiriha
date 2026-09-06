using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Metadata;
using Kiriha.Services.Data.Image;
using Kiriha.Services.Data.Settings;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Services.AppLifecycle;
using Kiriha.Core.Abstractions.Services;
using Serilog;

namespace Kiriha.Services.Data.Core;

public class LoadQueueService : ILoadQueueService, IDisposable
{
    private readonly PosterBatchDownloader _posterBatchDownloader;
    private readonly ShikiMetadataService _shikiMetadata;
    private readonly ISettingsService _settings;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;

    private const int ImageWorkerCount = 5;
    private const int ShikiWorkerCount = 2;
    private const int ImageQueueCapacity = 512;
    private const int ShikiQueueCapacity = 256;

    private readonly Lock _dedupeLock = new();
    private readonly HashSet<int> _queuedForImage = new();
    private readonly HashSet<int> _queuedForShiki = new();
    private readonly Channel<AnimeEntity> _imageQueue = CreateQueue(ImageQueueCapacity);
    private readonly Channel<AnimeEntity> _shikiQueue = CreateQueue(ShikiQueueCapacity);

    public LoadQueueService(
        PosterBatchDownloader posterBatchDownloader,
        ShikiMetadataService shikiMetadata,
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
            if (item == null) continue;

            bool needsImage = !string.IsNullOrEmpty(item.MainPictureUrl);
            bool needsShiki = (_settings.Current.UI.UseRussianTitles && string.IsNullOrEmpty(item.RussianTitle)) ||
                (_settings.Current.UI.UseRussianDescriptions && string.IsNullOrEmpty(item.RussianSynopsis));

            if (needsImage)
                EnqueueItem(item, _imageQueue.Writer, _queuedForImage);

            if (needsShiki)
                EnqueueItem(item, _shikiQueue.Writer, _queuedForShiki);
        }
    }

    public void ClearQueues()
    {
        lock (_dedupeLock)
        {
            while (_imageQueue.Reader.TryRead(out _)) { }
            while (_shikiQueue.Reader.TryRead(out _)) { }

            _queuedForImage.Clear();
            _queuedForShiki.Clear();
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

    private void EnqueueItem(AnimeEntity item, ChannelWriter<AnimeEntity> writer, HashSet<int> queuedIds)
    {
        lock (_dedupeLock)
        {
            if (!queuedIds.Add(item.Id))
                return;
        }

        if (writer.TryWrite(item))
            return;

        _ = EnqueueSlowAsync(item, writer, queuedIds);
    }

    private async Task EnqueueSlowAsync(AnimeEntity item, ChannelWriter<AnimeEntity> writer, HashSet<int> queuedIds)
    {
        try
        {
            await writer.WriteAsync(item);
        }
        catch (ChannelClosedException)
        {
            lock (_dedupeLock)
            {
                queuedIds.Remove(item.Id);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "LoadQueueService: failed to enqueue item {Id}", item.Id);
            lock (_dedupeLock)
            {
                queuedIds.Remove(item.Id);
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
                    RemoveQueuedId(_queuedForImage, b.Id);
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
                    RemoveQueuedId(_queuedForShiki, b.Id);
                }
            }
        }
    }

    private void RemoveQueuedId(HashSet<int> queuedIds, int id)
    {
        lock (_dedupeLock)
        {
            queuedIds.Remove(id);
        }
    }

    public void Dispose()
    {
        _imageQueue.Writer.TryComplete();
        _shikiQueue.Writer.TryComplete();
    }
}
