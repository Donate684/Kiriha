using System;
using System.Threading.Tasks;
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

            await _syncTaskRepo.UpdateAsync(MapToEntity(task));

            // Fire and forget a delayed re-enqueue to not block the main queue
            _ = _backgroundTasks.Run("SyncManager.DelayedRetry", async retryCt =>
            {
                await Task.Delay(TimeSpan.FromMinutes(delayMin), retryCt);
                _lowPriorityQueue.Enqueue(task);
                try { _queueSignal.Release(); } catch (ObjectDisposedException) { }
            }, _cts.Token);
        }
        else
        {
            Log.Warning("Task {TaskId} permanently failed after {MaxRetries} retries", task.Id, MaxRetries);
            await _syncTaskRepo.RemoveAsync(task.Id);
            _historyService.AddEntry(task.AnimeId, task.FullItem?.Title ?? $"ID {task.AnimeId}", null, 0, "SyncFailed", string.Format("sync.syncing.failed"));
        }
    }
}
