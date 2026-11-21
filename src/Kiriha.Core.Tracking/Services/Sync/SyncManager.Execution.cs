using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Auth;
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
                            await _syncTaskRepo.RemoveAsync(task.Id, ct);
                            Log.Information("Sync task {TaskId} completed successfully.", task.Id);
                        }
                        else if (!didExecute && task.Owner != null)
                        {
                            ScheduleRetry(task, AccountRetryDelay);
                        }
                        else
                        {
                            await HandleTaskFailureAsync(task);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        // Clean shutdown: do not call HandleTaskFailureAsync, break out preserving task
                        break;
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
        TrackerAccount? account = null;
        List<ITrackerService> activeTrackers;
        if (task.Owner is { } owner)
        {
            var tracker = GetOwnerTracker(owner);
            if (tracker != null && task.SuccessfulTrackers.Contains(tracker.Name))
                return (true, false);

            account = GetActiveOwner(owner, task.IsDirect);
            if (account is null || tracker is null)
                return (false, false);

            activeTrackers = [tracker];
        }
        else
        {
            if (_settingsService != null)
                return (false, false);
            activeTrackers = GetTaskTrackers(task);
        }

        if (_latestTaskIds.TryGetValue((task.AnimeId, task.MediaKind, task.Owner), out var latest) && latest.Id > task.Id)
        {
            if (latest.Type == SyncTaskType.Remove)
            {
                Log.Information("SyncManager: Skipping outdated task {TaskId} ({TaskType}) for Anime {AnimeId} because newer task {LatestId} is Remove.", task.Id, task.Type, task.AnimeId, latest.Id);
                await MarkSupersededAsync(task, ct);
                return (true, false);
            }

            if (task.Type == SyncTaskType.Remove)
            {
                // User re-added or updated this anime after deleting it earlier.
                // The earlier delayed Remove task must NOT run because it would delete the re-added title.
                Log.Information("SyncManager: Skipping outdated Remove task {TaskId} for Anime {AnimeId} because newer task {LatestId} ({LatestType}) exists.", task.Id, task.AnimeId, latest.Id, latest.Type);
                await MarkSupersededAsync(task, ct);
                return (true, false);
            }

            if (latest.Type == SyncTaskType.FullUpdate || task.Type != SyncTaskType.FullUpdate)
            {
                Log.Information("SyncManager: Skipping outdated task {TaskId} for Anime {AnimeId} because newer task {LatestId} is pending.", task.Id, task.AnimeId, latest.Id);
                if (task.HistoryId.HasValue)
                {
                    await MarkSupersededAsync(task, ct);
                }
                else if (task.Owner is null && task.Progress.HasValue)
                {
                    foreach (var tracker in activeTrackers)
                    {
                        UpdateHistoryTrackerStatus(task, tracker.Name, TrackerSyncState.Success);
                    }
                }
                return (true, false);
            }

            // Older FullUpdate retrying after a newer UpdateProgress: advance FullItem's progress/status/score
            // so retrying the FullUpdate never rolls remote progress backward.
            if (task.FullItem != null)
            {
                if (latest.Progress.HasValue)
                {
                    if (task.MediaKind != MediaKind.Anime)
                        task.FullItem.ChaptersRead = latest.Progress.Value;
                    else
                        task.FullItem.Progress = latest.Progress.Value;
                    task.Progress = latest.Progress;
                }

                if (latest.Status.HasValue && latest.Status.Value != UserAnimeStatus.None)
                {
                    task.FullItem.Status = latest.Status.Value;
                    task.Status = latest.Status;
                }

                if (latest.Score.HasValue)
                {
                    task.FullItem.Score = latest.Score.Value <= 0 ? "-" : latest.Score.Value.ToString();
                    task.Score = latest.Score;
                }
            }
        }

        bool overallSuccess = true;

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
            ct.ThrowIfCancellationRequested();
            if (task.SuccessfulTrackers.Contains(tracker.Name))
            {
                Log.Debug("Skipping {Tracker} for task {Id} as it was already successful", tracker.Name, task.Id);
                continue;
            }

            executedAny = true;
            SyncOutcome outcome;
            string? requestError = null;
            try
            {
                using var credentials = new TrackerRequestAccount(account);
                outcome = task.Type switch
                {
                    SyncTaskType.UpdateProgress => task.MediaKind != MediaKind.Anime
                        ? await tracker.UpdateMangaProgressAsync(
                            task.AnimeId,
                            task.Progress ?? 0,
                            task.FullItem?.VolumesRead,
                            task.Status,
                            task.Score,
                            ct)
                        : await tracker.UpdateProgressAsync(
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
                    SyncTaskType.Remove => task.MediaKind != MediaKind.Anime
                        ? await tracker.RemoveMangaAsync(task.AnimeId, ct)
                        : await tracker.RemoveAnimeAsync(task.AnimeId, ct),
                    _ => SyncOutcome.PermanentFailure
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error syncing with {Tracker}", tracker.Name);
                outcome = SyncOutcome.TransientFailure;
                requestError = ex.Message;
            }

            var (state, error) = outcome switch
            {
                SyncOutcome.Success => (TrackerSyncState.Success, (string?)null),
                SyncOutcome.PermanentFailure => (TrackerSyncState.Failed, "Permanent failure"),
                _ => (TrackerSyncState.Retrying, requestError ?? "Transient failure (will retry)")
            };
            bool resolved = outcome is SyncOutcome.Success or SyncOutcome.PermanentFailure;
            if (resolved)
                task.SuccessfulTrackers.Add(tracker.Name);
            else
                overallSuccess = false;

            // Keep an acknowledgement and its history status in one commit, even during shutdown.
            if (task.HistoryId.HasValue)
                await CheckpointDeliveryAsync(MapToEntity(task), tracker.Name, state, error, CancellationToken.None);
            else
            {
                UpdateHistoryTrackerStatus(task, tracker.Name, state, error);
                if (resolved)
                    await _syncTaskRepo.UpdateAsync(MapToEntity(task), CancellationToken.None);
            }

            if (outcome == SyncOutcome.PermanentFailure || (!resolved && task.RetryCount == 0))
            {
                _notificationService?.NotifySyncFailed(task.FullItem?.Title ?? $"ID {task.AnimeId}", tracker.Name,
                    resolved ? "Permanent failure" : requestError ?? "Network error", willRetry: !resolved);
            }

            Log.Information("{Tracker} sync for {Id}: {Outcome}", tracker.Name, task.AnimeId, outcome);
        }


        return (overallSuccess, executedAny);
    }

    private void UpdateHistoryTrackerStatus(SyncTask task, string trackerName, TrackerSyncState state, string? error = null)
    {
        if (task.Owner != null)
            return;

        _historyService.UpdateTrackerStatus(task.AnimeId, task.MediaKind, task.Progress, trackerName, state, error);
    }

    private Task MarkSupersededAsync(SyncTask task, CancellationToken ct) =>
        task.HistoryId.HasValue ? RecordDiscardedDeliveryAsync(MapToEntity(task), ct) : Task.CompletedTask;

    private List<ITrackerService> GetTaskTrackers(SyncTask task)
    {
        if (!string.IsNullOrEmpty(task.TargetTracker))
        {
            var targeted = _trackers
                .Where(t => t.IsEnabled && string.Equals(t.Name, task.TargetTracker, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return targeted;
        }

        return GetDestinationTrackers();
    }

    private List<ITrackerService> GetDestinationTrackers() => _trackers.Where(t => t.IsEnabled).ToList();
}
