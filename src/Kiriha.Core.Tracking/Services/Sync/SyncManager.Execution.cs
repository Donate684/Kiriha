using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Tracking.Sync.Models;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public partial class SyncManager
{
    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _queueSignal.WaitAsync(ct);
                if (_highPriorityQueue.TryDequeue(out var task) || _lowPriorityQueue.TryDequeue(out task))
                {
                    bool executed = true;
                    try
                    {
                        var (success, didExecute) = await ExecuteTaskAsync(task, ct);
                        executed = didExecute;

                        if (success)
                        {
                            await _syncTaskRepo.RemoveAsync(task.Id);
                            Log.Information("Sync task {TaskId} completed successfully.", task.Id);
                        }
                        else
                        {
                            await HandleTaskFailureAsync(task);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error processing sync task {Id}", task.Id);
                    }

                    if (executed)
                    {
                        await Task.Delay(DelayBetweenRequestsMs, ct);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Error(ex, "SyncManager error"); }
    }

    private async Task<(bool Success, bool Executed)> ExecuteTaskAsync(SyncTask task, CancellationToken ct)
    {
        if (_latestTaskIds.TryGetValue(task.AnimeId, out var latest) && latest.Id > task.Id)
        {
            if (latest.Type == SyncTaskType.Remove)
            {
                Log.Information("SyncManager: Skipping outdated task {TaskId} ({TaskType}) for Anime {AnimeId} because newer task {LatestId} is Remove.", task.Id, task.Type, task.AnimeId, latest.Id);
                return (true, false);
            }

            // Optimization: If a newer FULL update is pending, we can skip this task.
            // BUT: If this is a FullUpdate and the latest is just a Progress update, we SHOULD NOT skip,
            // because the FullUpdate might contain data (notes/dates) that Progress update doesn't have.

            // We only skip if the newer task is of the same or "broader" type.
            // In our case, FullUpdate is the broadest. Remove must always run - it's the
            // user-facing intent and skipping it would silently keep the entry on the tracker.
            if (task.Type != SyncTaskType.FullUpdate && task.Type != SyncTaskType.Remove)
            {
                Log.Information("SyncManager: Skipping outdated task {TaskId} for Anime {AnimeId} because newer task {LatestId} is pending.", task.Id, task.AnimeId, latest.Id);
                if (task.Progress.HasValue)
                {
                    foreach (var tracker in _trackers.Where(t => t.IsEnabled))
                    {
                        _historyService.UpdateTrackerStatus(task.AnimeId, task.Progress, tracker.Name, TrackerSyncState.Success);
                    }
                }
                return (true, false);
            }
        }

        bool overallSuccess = true;
        var activeTrackers = GetDestinationTrackers();

        if (activeTrackers.Count == 0)
        {
            if (_trackers.Count == 0)
            {
                Log.Warning("SyncManager: No trackers registered in the system!");
            }
            else
            {
                var names = string.Join(", ", _trackers.Select(t => t.Name));
                Log.Warning("SyncManager: No active (logged in) trackers for sync sync task. Registered trackers: {Trackers}", names);
            }
            return (true, false); // Nothing to do; count as success to avoid retrying.
        }

        bool executedAny = false;
        foreach (var tracker in activeTrackers)
        {
            if (task.SuccessfulTrackers.Contains(tracker.Name))
            {
                Log.Debug("Skipping {Tracker} for task {Id} as it was already successful", tracker.Name, task.Id);
                continue;
            }

            executedAny = true;
            try
            {
                SyncOutcome outcome = task.Type switch
                {
                    SyncTaskType.UpdateProgress => await tracker.UpdateProgressAsync(
                        task.AnimeId,
                        task.Progress ?? 0,
                        task.Status,
                        task.Score,
                        task.FullItem?.IsRewatching,
                        task.FullItem?.RewatchCount,
                        ct),
                    SyncTaskType.FullUpdate => task.FullItem != null
                        ? await tracker.SaveFullListStatusAsync(task.FullItem, ct)
                        : SyncOutcome.PermanentFailure,
                    SyncTaskType.Remove => await tracker.RemoveAnimeAsync(task.AnimeId, ct),
                    _ => SyncOutcome.PermanentFailure
                };


                // Both Success and PermanentFailure are "resolved" - add the tracker
                // to SuccessfulTrackers so the next retry pass skips it. The DB schema
                // calls the column SuccessfulTrackers but the semantic is really
                // "trackers we should not call again for this task".
                if (outcome == SyncOutcome.Success)
                {
                    task.SuccessfulTrackers.Add(tracker.Name);
                    _historyService.UpdateTrackerStatus(task.AnimeId, task.Progress, tracker.Name, TrackerSyncState.Success);
                }
                else if (outcome == SyncOutcome.PermanentFailure)
                {
                    task.SuccessfulTrackers.Add(tracker.Name);
                    _historyService.UpdateTrackerStatus(task.AnimeId, task.Progress, tracker.Name, TrackerSyncState.Failed, "Permanent failure");
                    _notificationService?.NotifySyncFailed(task.FullItem?.Title ?? $"ID {task.AnimeId}", tracker.Name, "Permanent failure", willRetry: false);
                }
                else
                {
                    // TransientFailure - leave out of SuccessfulTrackers, mark task for retry.
                    overallSuccess = false;
                    _historyService.UpdateTrackerStatus(task.AnimeId, task.Progress, tracker.Name, TrackerSyncState.Retrying, "Transient failure (will retry)");
                    if (task.RetryCount == 0)
                    {
                        _notificationService?.NotifySyncFailed(task.FullItem?.Title ?? $"ID {task.AnimeId}", tracker.Name, "Network error", willRetry: true);
                    }
                }

                Log.Information("{Tracker} sync for {Id}: {Outcome}", tracker.Name, task.AnimeId, outcome);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error syncing with {Tracker}", tracker.Name);
                overallSuccess = false;
                _historyService.UpdateTrackerStatus(task.AnimeId, task.Progress, tracker.Name, TrackerSyncState.Retrying, ex.Message);
                if (task.RetryCount == 0)
                {
                    _notificationService?.NotifySyncFailed(task.FullItem?.Title ?? $"ID {task.AnimeId}", tracker.Name, ex.Message, willRetry: true);
                }
            }
        }


        return (overallSuccess, executedAny);
    }

    private List<ITrackerService> GetDestinationTrackers()
    {
        var active = _trackers.Where(t => t.IsEnabled).ToList();
        if (_settingsService == null) return active;

        var api = _settingsService.Current.Api;
        var primary = api.GetPrimaryAccount();
        var mirrors = api.GetMirrorAccounts().ToList();

        return active.Where(tracker =>
        {
            if (primary != null && string.Equals(tracker.TrackerId, primary.TrackerId, StringComparison.OrdinalIgnoreCase))
                return true;

            return mirrors.Any(m => string.Equals(tracker.TrackerId, m.TrackerId, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }
}
