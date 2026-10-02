using Kiriha.Core.Domain.Models.Entities;
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

    public async Task EnqueueUpdateAsync(int animeId, int progress, UserAnimeStatus? status = null, int? score = null, string? targetTracker = null)
    {
        var task = new SyncTask
        {
            AnimeId = animeId,
            Type = SyncTaskType.UpdateProgress,
            Progress = progress,
            Status = status,
            Score = score
        };

        var activeTrackers = _trackers.Where(t => t.IsEnabled).Select(t => t.Name).ToList();

        // If a specific tracker is targeted, skip all other active trackers for this task
        if (!string.IsNullOrEmpty(targetTracker))
        {
            foreach (var other in activeTrackers.Where(name => !string.Equals(name, targetTracker, StringComparison.OrdinalIgnoreCase)))
            {
                task.SuccessfulTrackers.Add(other);
            }
        }

        var entity = MapToEntity(task);
        task.Id = await _syncTaskRepo.AddAsync(entity);

        _latestTaskIds[animeId] = (task.Id, task.Type);
        try
        {
            var pendingTrackers = !string.IsNullOrEmpty(targetTracker)
                ? new List<string> { targetTracker }
                : activeTrackers;

            if (pendingTrackers.Count > 0)
            {
                _historyService.SetPendingTrackers(animeId, progress, pendingTrackers);
            }

            _highPriorityQueue.Enqueue(task);
            _queueSignal.Release();
            Log.Information("Sync task enqueued (DB ID: {Id}): UpdateProgress for {AnimeId} to {Progress} (Target: {Target})",
                task.Id, animeId, progress, targetTracker ?? "All");
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
            FullItem = item,
            Progress = item.Progress,
            Status = item.Status
        };
        var entity = MapToEntity(task);
        task.Id = await _syncTaskRepo.AddAsync(entity);

        _latestTaskIds[item.Id] = (task.Id, task.Type);
        try
        {
            var activeTrackers = _trackers.Where(t => t.IsEnabled).Select(t => t.Name).ToList();
            if (activeTrackers.Count > 0)
            {
                _historyService.SetPendingTrackers(item.Id, item.Progress, activeTrackers);
            }

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
