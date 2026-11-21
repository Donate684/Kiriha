using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync.Models;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public partial class SyncManager
{
    private readonly record struct LatestSyncTaskSnapshot(
        int Id,
        SyncTaskType Type,
        int? Progress,
        UserAnimeStatus? Status,
        int? Score);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int AnimeId, MediaKind Kind, SyncTaskOwner? Owner), LatestSyncTaskSnapshot> _latestTaskIds = new();

    private void RecordLatestTask(SyncTask task)
    {
        var snap = new LatestSyncTaskSnapshot(task.Id, task.Type, task.Progress, task.Status, task.Score);
        _latestTaskIds.AddOrUpdate(
            (task.AnimeId, task.MediaKind, task.Owner),
            snap,
            (_, existing) => snap.Id >= existing.Id ? snap : existing);
    }

    private static MediaKind ExtractPayloadMediaKind(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
            return MediaKind.Anime;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(nameof(AnimeEntity.MediaKind), out var prop))
            {
                if (prop.ValueKind == System.Text.Json.JsonValueKind.Number && prop.TryGetInt32(out var kindInt) && Enum.IsDefined(typeof(MediaKind), kindInt))
                    return (MediaKind)kindInt;

                if (prop.ValueKind == System.Text.Json.JsonValueKind.String && Enum.TryParse<MediaKind>(prop.GetString(), ignoreCase: true, out var parsedKind))
                    return parsedKind;
            }
        }
        catch
        {
            // Fallback to Anime
        }

        return MediaKind.Anime;
    }

    private static MediaKind ResolveEntityMediaKind(SyncTaskEntity entity) =>
        entity.MediaKind != MediaKind.Anime ? entity.MediaKind : ExtractPayloadMediaKind(entity.Payload);

    private List<SyncTaskEntity> DeduplicateStartupTasks(List<SyncTaskEntity> pendingTasks)
    {
        // A Remove supersedes earlier edits for the same title and recipient.
        return pendingTasks
            .GroupBy(t => (t.AnimeId, Kind: ResolveEntityMediaKind(t), Owner: OwnedSyncTaskPayload.Read(t.Payload)?.Owner))
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
                var latestFullUpdate = latestTasks.Find(x => x.Type == nameof(SyncTaskType.FullUpdate));
                if (latestFullUpdate != null)
                {
                    // Keep newer progress so replay can apply it to the older full item.
                    latestTasks.RemoveAll(x => x.Type == nameof(SyncTaskType.UpdateProgress) && x.Id < latestFullUpdate.Id);
                }
                return latestTasks;
            })
            .OrderBy(x => x.Id)
            .ToList();
    }

    public async Task EnqueueUpdateAsync(int animeId, MediaKind mediaKind, int progress, UserAnimeStatus? status = null, int? score = null, string? targetTracker = null)
    {
        var task = CreateProgressTask(animeId, mediaKind, progress, status, score, targetTracker);
        if (_settingsService != null)
        {
            await EnqueueOwnedMutationAsync(task);
            return;
        }

        var destinationTrackers = GetDestinationTrackers().Select(t => t.Name).ToList();
        var allEnabledTrackers = _trackers.Where(t => t.IsEnabled).Select(t => t.Name).ToList();

        // If a specific tracker is targeted, skip all other active trackers for this task
        if (!string.IsNullOrEmpty(targetTracker))
        {
            foreach (var other in allEnabledTrackers.Where(name => !string.Equals(name, targetTracker, StringComparison.OrdinalIgnoreCase)))
            {
                task.SuccessfulTrackers.Add(other);
            }
        }

        var entity = MapToEntity(task);
        task.Id = await _syncTaskRepo.AddAsync(entity, _cts.Token);

        RecordLatestTask(task);
        try
        {
            var pendingTrackers = !string.IsNullOrEmpty(targetTracker)
                ? new List<string> { targetTracker }
                : destinationTrackers;

            if (pendingTrackers.Count > 0)
            {
#pragma warning disable S6966 // Synchronous fire-and-forget history logging
                _historyService.SetPendingTrackers(animeId, mediaKind, progress, pendingTrackers);
#pragma warning restore S6966
            }

            _highPriorityQueue.Enqueue(task);
            _queueSignal.Release();
            Log.Information("Sync task enqueued (DB ID: {Id}): UpdateProgress for {AnimeId} ({MediaKind}) to {Progress} (Target: {Target})",
                task.Id, animeId, mediaKind, progress, targetTracker ?? "All");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enqueue task (DB ID: {Id})", task.Id);
        }
    }

    public async Task EnqueueRemoveAsync(int animeId, MediaKind mediaKind)
    {
        var task = new SyncTask
        {
            AnimeId = animeId,
            MediaKind = mediaKind,
            Type = SyncTaskType.Remove,
            FullItem = mediaKind != MediaKind.Anime ? new AnimeEntity { Id = animeId, MediaKind = mediaKind } : null
        };
        if (_settingsService != null)
        {
            await EnqueueOwnedMutationAsync(task);
            return;
        }
        var entity = MapToEntity(task);
        task.Id = await _syncTaskRepo.AddAsync(entity, _cts.Token);

        RecordLatestTask(task);
        try
        {
            _highPriorityQueue.Enqueue(task);
            _queueSignal.Release();
            Log.Information("Sync task enqueued (DB ID: {Id}): Remove for {AnimeId} ({MediaKind})", task.Id, animeId, mediaKind);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enqueue task (DB ID: {Id})", task.Id);
        }
    }

    public async Task EnqueueFullUpdateAsync(AnimeEntity item)
    {
        var task = CreateFullUpdateTask(item);
        if (_settingsService != null)
        {
            await EnqueueOwnedMutationAsync(task);
            return;
        }
        var entity = MapToEntity(task);
        task.Id = await _syncTaskRepo.AddAsync(entity, _cts.Token);

        RecordLatestTask(task);
        try
        {
            var destinationTrackers = GetDestinationTrackers().Select(t => t.Name).ToList();
            if (destinationTrackers.Count > 0)
            {
#pragma warning disable S6966 // Synchronous fire-and-forget history logging
                _historyService.SetPendingTrackers(item.Id, item.MediaKind, task.Progress, destinationTrackers);
#pragma warning restore S6966
            }

            _highPriorityQueue.Enqueue(task);
            _queueSignal.Release();
            Log.Information("Sync task enqueued (DB ID: {Id}): FullUpdate for {AnimeId} ({MediaKind})", task.Id, item.Id, item.MediaKind);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enqueue task (DB ID: {Id})", task.Id);
        }
    }

    public async Task<bool> CommitProgressChangeAsync(
        AnimeEntity item, UserAnimeStatus? nextStatus, HistoryItem history, bool onlyAdvance, CancellationToken ct)
    {
        var repository = _progressChangeRepository ?? throw new InvalidOperationException("Progress persistence is not configured.");
        var task = item.MediaKind != MediaKind.Anime || nextStatus.HasValue
            ? CreateFullUpdateTask(item)
            : CreateProgressTask(item.Id, item.MediaKind, history.Episode,
                item.Status == UserAnimeStatus.None ? null : item.Status);
        var destinations = BindDestinations(task);
        AttachOriginalDeliveries(history, destinations);
        if (_settingsService == null)
        {
            foreach (var tracker in GetDestinationTrackers())
                history.SetTrackerStatus(tracker.Name, TrackerSyncState.Pending);
        }

        var entities = destinations.Select(x => MapToEntity(x.Task)).ToArray();
        await _dbInit.InitializationTask.WaitAsync(ct);
        await _queuePersistenceGate.WaitAsync(ct);
        try
        {
            if (!await repository.SaveAsync(item, nextStatus, history, entities, onlyAdvance, ct))
                return false;

            _historyService.PublishCommittedEntry(history);
            QueueCommittedDestinations(destinations, entities);
            return true;
        }
        finally
        {
            _queuePersistenceGate.Release();
        }
    }

    private void QueueCommittedDestinations(
        IReadOnlyList<(SyncTask Task, string? Name)> destinations, IReadOnlyList<SyncTaskEntity> entities)
    {
        // Before startup restoration, SQLite is the only source of work. This prevents
        // queuing the same row twice if a write lands as the worker starts.
        if (_settingsService != null && !_restoreFinished)
            return;

        for (var index = 0; index < entities.Count; index++)
        {
            var task = destinations[index].Task;
            task.Id = entities[index].Id;
            task.HistoryId = OwnedSyncTaskPayload.Read(entities[index].Payload)?.HistoryId;
            RecordLatestTask(task);
            if (_isStopped)
                continue;

            _highPriorityQueue.Enqueue(task);
            try { _queueSignal.Release(); }
            catch (ObjectDisposedException) when (_isStopped)
            {
                // The committed task will be restored when the application next starts.
            }
        }
    }

    private static SyncTask CreateProgressTask(
        int animeId, MediaKind mediaKind, int progress, UserAnimeStatus? status, int? score = null, string? targetTracker = null) => new()
    {
        AnimeId = animeId,
        MediaKind = mediaKind,
        Type = SyncTaskType.UpdateProgress,
        Progress = progress,
        Status = status,
        Score = score,
        TargetTracker = targetTracker
    };

    private static SyncTask CreateFullUpdateTask(AnimeEntity item) => new()
    {
        AnimeId = item.Id,
        MediaKind = item.MediaKind,
        Type = SyncTaskType.FullUpdate,
        FullItem = item,
        Progress = item.MediaKind == MediaKind.Anime ? item.Progress : item.ChaptersRead,
        Status = item.Status
    };
}
