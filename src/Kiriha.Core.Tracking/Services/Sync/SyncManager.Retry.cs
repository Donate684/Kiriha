using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync.Models;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public partial class SyncManager
{
    private async Task HandleTaskFailureAsync(SyncTask task)
    {
        task.RetryCount++;
        if (task.RetryCount < MaxRetries)
        {
            int delayMin = (int)Math.Pow(2, task.RetryCount); // 2, 4, 8, 16 min...
            Log.Warning("Task {TaskId} failed (attempt {Attempt}/{Max}), will retry in {Delay} min.",
                task.Id, task.RetryCount, MaxRetries, delayMin);

            await _syncTaskRepo.UpdateAsync(MapToEntity(task), _cts.Token);

            // Fire and forget a delayed re-enqueue to not block the main queue
            var retryDelay = TimeSpan.FromMinutes(delayMin);
            _ = _backgroundTasks.Run("SyncManager.DelayedRetry", async retryCt =>
            {
                await Task.Delay(retryDelay, retryCt);
                _lowPriorityQueue.Enqueue(task);
                try { _queueSignal.Release(); } catch (ObjectDisposedException) { }
            }, _cts.Token);
        }
        else
        {
            Log.Warning("Task {TaskId} permanently failed after {MaxRetries} retries", task.Id, MaxRetries);
            await _syncTaskRepo.RemoveAsync(task.Id, _cts.Token);

            var failedTrackers = GetTaskTrackers(task)
                .Where(t => !task.SuccessfulTrackers.Contains(t.Name))
                .Select(t => t.Name)
                .ToList();
            foreach (var trackerName in failedTrackers)
            {
                UpdateHistoryTrackerStatus(task, trackerName, TrackerSyncState.Failed, "Max retries exceeded");
                _notificationService?.NotifySyncFailed(task.FullItem?.Title ?? $"ID {task.AnimeId}", trackerName, "Max retries exceeded", willRetry: false);
            }

            var detail = _localizer.GetLoc("sync.syncing.failed");
#pragma warning disable S6966 // Synchronous fire-and-forget history logging
            _historyService.AddEntry(task.AnimeId, task.MediaKind, task.FullItem?.Title ?? $"ID {task.AnimeId}", null, 0, HistoryActionType.SyncFailed, detail);
#pragma warning restore S6966
        }
    }
}
