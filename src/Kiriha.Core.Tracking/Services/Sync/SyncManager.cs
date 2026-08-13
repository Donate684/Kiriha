using Kiriha.Core.Services;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Sync.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repository;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;


/// <summary>
/// Replays pending tracker mutations from <c>sync_tasks</c> on startup and
/// drains a bounded in-memory queue for live mutations enqueued during the
/// session.
///
/// Lifecycle is owned by the host: <see cref="StartAsync"/> kicks off the
/// background loop, <see cref="StopAsync"/> stops accepting new items and
/// awaits the in-flight task to finish (bounded by the host's stop token).
/// Dropped the old <c>Task.Run</c>-in-ctor pattern so DI never silently
/// observes a half-constructed service running work against unrelated
/// dependencies on shutdown.
/// </summary>
public partial class SyncManager : IHostedService
{
    private readonly IReadOnlyList<ITrackerService> _trackers;
    private readonly ISyncTaskRepository _syncTaskRepo;
    private readonly Kiriha.Core.Services.IDatabaseInitializer _dbInit;
    private readonly Kiriha.Core.Services.IHistoryService _historyService;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly System.Collections.Concurrent.ConcurrentQueue<SyncTask> _highPriorityQueue = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<SyncTask> _lowPriorityQueue = new();
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;
    private const int MaxRetries = 5;
    private const int DelayBetweenRequestsMs = 1500;

    public SyncManager(
        IEnumerable<ITrackerService> trackers,
        ISyncTaskRepository syncTaskRepo,
        Kiriha.Core.Services.IDatabaseInitializer dbInit,
        Kiriha.Core.Services.IHistoryService historyService,
        IBackgroundTaskSupervisor backgroundTasks)
    {
        _trackers = trackers.ToList();
        _syncTaskRepo = syncTaskRepo;
        _dbInit = dbInit;
        _historyService = historyService;
        _backgroundTasks = backgroundTasks;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_loopTask != null) return Task.CompletedTask;
        _loopTask = _backgroundTasks.Run("SyncManager.QueueLoop", InitializeAndProcessQueueAsync, _cts.Token);
        return Task.CompletedTask;
    }

    private volatile bool _isStopped = false;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_isStopped) return;
        _isStopped = true;

        _cts.Cancel();
        if (_loopTask != null)
        {
            try { await _loopTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { /* host gave up waiting */ }
            catch (Exception ex) { Log.Warning(ex, "SyncManager: loop task ended with an exception"); }
        }
        if (_loopTask == null || _loopTask.IsCompleted)
        {
            _cts.Dispose();
            _queueSignal.Dispose();
        }
    }

    private async Task InitializeAndProcessQueueAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            await _dbInit.InitializationTask;
            ct.ThrowIfCancellationRequested();
            var pendingTasks = await _syncTaskRepo.GetPendingAsync();

            var deduplicated = DeduplicateStartupTasks(pendingTasks);

            int restoredCount = 0;
            foreach (var entity in deduplicated)
            {
                try
                {
                    if (!Enum.TryParse<SyncTaskType>(entity.Type, out var type))
                    {
                        Log.Warning("Skipping sync task {Id} due to invalid type {Type}", entity.Id, entity.Type);
                        continue;
                    }

                    var task = new SyncTask
                    {
                        Id = entity.Id,
                        AnimeId = entity.AnimeId,
                        Type = type,
                        Progress = entity.Progress,
                        Status = entity.Status != null ? StatusMapper.FromDbString(entity.Status) : null,
                        Score = entity.Score,
                        RetryCount = entity.RetryCount
                    };
                    if (!string.IsNullOrEmpty(entity.SuccessfulTrackersJson))
                    {
                        var trackers = JsonSerializer.Deserialize<HashSet<string>>(entity.SuccessfulTrackersJson);
                        if (trackers != null) task.SuccessfulTrackers = trackers;
                    }
                    if (!string.IsNullOrEmpty(entity.Payload))
                    {
                        task.FullItem = JsonSerializer.Deserialize<AnimeEntity>(entity.Payload);
                    }
                    _latestTaskIds.AddOrUpdate(task.AnimeId, (task.Id, task.Type), (k, existing) => task.Id > existing.Id ? (task.Id, task.Type) : existing);
                    _lowPriorityQueue.Enqueue(task);
                    _queueSignal.Release();
                    restoredCount++;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to parse sync task {Id}", entity.Id);
                }
            }

            // Remove the tasks we skipped via deduplication
            var skippedIds = pendingTasks.Select(x => x.Id).Except(deduplicated.Select(x => x.Id)).ToList();
            if (skippedIds.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                await _syncTaskRepo.RemoveManyAsync(skippedIds);
            }

            if (restoredCount > 0) Log.Information("Restored {Count} pending sync tasks from database.", restoredCount);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load pending sync tasks from database");
        }

        await ProcessQueueAsync(ct);
    }

    private SyncTaskEntity MapToEntity(SyncTask task)
    {
        return new SyncTaskEntity
        {
            Id = task.Id,
            AnimeId = task.AnimeId,
            Type = task.Type.ToString(),
            Progress = task.Progress,
            Status = task.Status != null ? StatusMapper.ToDbString(task.Status.Value) : null,
            Score = task.Score,
            Payload = task.FullItem != null ? JsonSerializer.Serialize(task.FullItem, (JsonSerializerOptions?)null) : null,
            RetryCount = task.RetryCount,
            SuccessfulTrackersJson = task.SuccessfulTrackers.Count > 0 ? JsonSerializer.Serialize(task.SuccessfulTrackers, (JsonSerializerOptions?)null) : null
        };
    }
}
