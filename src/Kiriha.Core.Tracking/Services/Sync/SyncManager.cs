using System.Text.Json;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync.Models;
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
public partial class SyncManager : ISyncManager, IHostedService
{
    private readonly IReadOnlyList<ITrackerService> _trackers;
    private readonly ISyncTaskRepository _syncTaskRepo;
    private readonly IProgressChangeRepository? _progressChangeRepository;
    private readonly IDatabaseInitializer _dbInit;
    private readonly IHistoryService _historyService;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly INotificationService? _notificationService;
    private readonly ISettingsService? _settingsService;
    private readonly TimeProvider _retryClock;
    private readonly System.Collections.Concurrent.ConcurrentQueue<SyncTask> _highPriorityQueue = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<SyncTask> _lowPriorityQueue = new();
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly SemaphoreSlim _queuePersistenceGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private bool _restoreFinished;
    private Task? _loopTask;
    private const int RetryBackoffLimit = 5;
    private const int DelayBetweenRequestsMs = 1500;

    public SyncManager(
        IEnumerable<ITrackerService> trackers,
        ISyncTaskRepository syncTaskRepo,
        IDatabaseInitializer dbInit,
        IHistoryService historyService,
        IBackgroundTaskSupervisor backgroundTasks,
        INotificationService? notificationService = null,
        ISettingsService? settingsService = null,
        IProgressChangeRepository? progressChangeRepository = null,
        TimeProvider? retryClock = null)
    {
        _trackers = trackers.ToList();
        _syncTaskRepo = syncTaskRepo;
        _progressChangeRepository = progressChangeRepository;
        _dbInit = dbInit;
        _historyService = historyService;
        _backgroundTasks = backgroundTasks;
        _notificationService = notificationService;
        _settingsService = settingsService;
        _retryClock = retryClock ?? TimeProvider.System;
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

        await _cts.CancelAsync();
        if (_loopTask != null)
        {
            try { await _loopTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { /* host gave up waiting */ }
            catch (Exception ex) { Log.Warning(ex, "SyncManager: loop task ended with an exception"); }
        }
        if (_loopTask is null || _loopTask.IsCompleted)
        {
            _cts.Dispose();
            _queueSignal.Dispose();
        }
    }

    private async Task InitializeAndProcessQueueAsync(CancellationToken ct)
    {
        try
        {
            await _dbInit.InitializationTask.WaitAsync(ct);
            await _queuePersistenceGate.WaitAsync(ct);
            try
            {
                await RestorePendingTasksAsync(ct);
            }
            finally
            {
                _restoreFinished = true;
                _queuePersistenceGate.Release();
            }
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

    private async Task RestorePendingTasksAsync(CancellationToken ct)
    {
        var pendingTasks = await _syncTaskRepo.GetPendingAsync(ct);

        var replayable = _settingsService is null
            ? pendingTasks
            : pendingTasks.Where(entity => OwnedSyncTaskPayload.Read(entity.Payload) is not null).ToList();
        if (replayable.Count < pendingTasks.Count)
        {
            Log.Warning("SyncManager: retained {Count} pending tasks with no verified recipient", pendingTasks.Count - replayable.Count);
        }
        var deduplicated = DeduplicateStartupTasks(replayable);

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

                var payload = OwnedSyncTaskPayload.Read(entity.Payload);
                var resolvedKind = ResolveEntityMediaKind(entity);

                var task = new SyncTask
                {
                    Id = entity.Id,
                    AnimeId = entity.AnimeId,
                    MediaKind = resolvedKind,
                    Type = type,
                    Progress = entity.Progress,
                    Status = entity.Status != null ? StatusMapper.FromDbString(entity.Status) : null,
                    Score = entity.Score,
                    RetryCount = entity.RetryCount,
                    Owner = payload?.Owner,
                    IsDirect = payload?.IsDirect ?? false,
                    HistoryId = payload?.HistoryId
                };
                if (!string.IsNullOrEmpty(entity.SuccessfulTrackersJson))
                {
                    var trackers = JsonSerializer.Deserialize<HashSet<string>>(entity.SuccessfulTrackersJson);
                    if (trackers != null) task.SuccessfulTrackers = trackers;
                }
                if (payload != null)
                {
                    task.FullItem = payload.Item;
                }
                else if (!string.IsNullOrEmpty(entity.Payload))
                {
                    task.FullItem = JsonSerializer.Deserialize<AnimeEntity>(entity.Payload);
                    if (task.FullItem != null && entity.MediaKind == MediaKind.Anime)
                    {
                        task.MediaKind = task.FullItem.MediaKind;
                    }
                }
                RecordLatestTask(task);
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
        var keptIds = deduplicated.Select(x => x.Id).ToHashSet();
        var skipped = replayable.Where(x => !keptIds.Contains(x.Id)).ToList();
        if (skipped.Count > 0)
        {
            foreach (var entity in skipped)
                await RecordDiscardedDeliveryAsync(entity, ct);
            await _syncTaskRepo.RemoveManyAsync(skipped.Select(x => x.Id), ct);
        }

        if (restoredCount > 0) Log.Information("Restored {Count} pending sync tasks from database.", restoredCount);
    }

    private SyncTaskEntity MapToEntity(SyncTask task)
    {
        return new SyncTaskEntity
        {
            Id = task.Id,
            AnimeId = task.AnimeId,
            MediaKind = task.MediaKind,
            Type = task.Type.ToString(),
            Progress = task.Progress,
            Status = task.Status != null ? StatusMapper.ToDbString(task.Status.Value) : null,
            Score = task.Score,
            Payload = task.Owner is { } owner
                ? JsonSerializer.Serialize(new OwnedSyncTaskPayload(owner, task.FullItem, task.IsDirect, task.HistoryId))
                : task.FullItem != null ? JsonSerializer.Serialize(task.FullItem) : null,
            RetryCount = task.RetryCount,
            SuccessfulTrackersJson = task.SuccessfulTrackers.Count > 0 ? JsonSerializer.Serialize(task.SuccessfulTrackers, (JsonSerializerOptions?)null) : null
        };
    }
}
