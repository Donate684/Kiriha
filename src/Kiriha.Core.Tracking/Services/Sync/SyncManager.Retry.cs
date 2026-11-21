using Kiriha.Core.Tracking.Sync.Models;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public partial class SyncManager
{
    private static readonly TimeSpan AccountRetryDelay = TimeSpan.FromMinutes(1);

    private async Task HandleTaskFailureAsync(SyncTask task)
    {
        // Keep retrying transient failures. Cap the persisted counter and the delay
        // so a long outage cannot overflow them or silently discard the edit.
        task.RetryCount = Math.Clamp(task.RetryCount, 0, RetryBackoffLimit - 1) + 1;
        var retryDelay = TimeSpan.FromMinutes(1 << task.RetryCount);
        Log.Warning("Sync task {TaskId} is still pending; next retry in {Delay}", task.Id, retryDelay);

        await _syncTaskRepo.UpdateAsync(MapToEntity(task), _cts.Token);

        ScheduleRetry(task, retryDelay);
    }

    private void ScheduleRetry(SyncTask task, TimeSpan retryDelay)
    {
        _ = _backgroundTasks.Run("SyncManager.DelayedRetry", async retryCt =>
        {
            await Task.Delay(retryDelay, _retryClock, retryCt);
            _lowPriorityQueue.Enqueue(task);
            try { _queueSignal.Release(); } catch (ObjectDisposedException) { }
        }, _cts.Token);
    }
}
