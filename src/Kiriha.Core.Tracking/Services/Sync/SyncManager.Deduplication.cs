using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Core.Tracking.Sync.Models;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public partial class SyncManager
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, (int Id, SyncTaskType Type)> _latestTaskIds = new();

    private List<SyncTaskEntity> DeduplicateStartupTasks(List<SyncTaskEntity> pendingTasks)
    {
        // Deduplicate: take the LATEST task of each type per AnimeId to avoid redundant work.
        // If a 'Remove' task exists, it supersedes all prior tasks for that AnimeId.
        return pendingTasks
            .GroupBy(t => t.AnimeId)
            .SelectMany(g =>
            {
                var tasks = g.OrderBy(x => x.Id).ToList();
                var lastRemoveIndex = tasks.FindLastIndex(x => x.Type == nameof(SyncTaskType.Remove));
                if (lastRemoveIndex >= 0)
                {
                    tasks = tasks.Skip(lastRemoveIndex).ToList();
                }
                var latestTasks = tasks
                    .GroupBy(x => x.Type)
                    .Select(typeGroup => typeGroup.Last())
                    .ToList();
                if (latestTasks.Any(x => x.Type == nameof(SyncTaskType.FullUpdate)))
                {
                    latestTasks.RemoveAll(x => x.Type == nameof(SyncTaskType.UpdateProgress));
                }
                return latestTasks;
            })
            .OrderBy(x => x.Id)
            .ToList();
    }

    public async Task EnqueueUpdateAsync(int animeId, int progress, UserAnimeStatus? status = null, int? score = null)
    {
        var task = new SyncTask
        {
            AnimeId = animeId,
            Type = SyncTaskType.UpdateProgress,
            Progress = progress,
            Status = status,
            Score = score
        };
        var entity = MapToEntity(task);
        task.Id = await _syncTaskRepo.AddAsync(entity);

        _latestTaskIds[animeId] = (task.Id, task.Type);
        try
        {
            _highPriorityQueue.Enqueue(task);
            _queueSignal.Release();
            Log.Information("Sync task enqueued (DB ID: {Id}): UpdateProgress for {AnimeId} to {Progress}", task.Id, animeId, progress);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enqueue task (DB ID: {Id})", task.Id);
        }
    }

    public async Task EnqueueRemoveAsync(int animeId)
    {
        var task = new SyncTask
        {
            AnimeId = animeId,
            Type = SyncTaskType.Remove
        };
        var entity = MapToEntity(task);
        task.Id = await _syncTaskRepo.AddAsync(entity);

        _latestTaskIds[animeId] = (task.Id, task.Type);
        try
        {
            _highPriorityQueue.Enqueue(task);
            _queueSignal.Release();
            Log.Information("Sync task enqueued (DB ID: {Id}): Remove for {AnimeId}", task.Id, animeId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enqueue task (DB ID: {Id})", task.Id);
        }
    }

    public async Task EnqueueFullUpdateAsync(AnimeEntity item)
    {
        var task = new SyncTask
        {
            AnimeId = item.Id,
            Type = SyncTaskType.FullUpdate,
            FullItem = item
        };
        var entity = MapToEntity(task);
        task.Id = await _syncTaskRepo.AddAsync(entity);

        _latestTaskIds[item.Id] = (task.Id, task.Type);
        try
        {
            _highPriorityQueue.Enqueue(task);
            _queueSignal.Release();
            Log.Information("Sync task enqueued (DB ID: {Id}): FullUpdate for {AnimeId}", task.Id, item.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enqueue task (DB ID: {Id})", task.Id);
        }
    }
}
