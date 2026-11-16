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

    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int AnimeId, MediaKind Kind), LatestSyncTaskSnapshot> _latestTaskIds = new();

    private void RecordLatestTask(SyncTask task)
    {
        var snap = new LatestSyncTaskSnapshot(task.Id, task.Type, task.Progress, task.Status, task.Score);
        _latestTaskIds.AddOrUpdate(
            (task.AnimeId, task.MediaKind),
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
        // Deduplicate: take the LATEST task of each type per (AnimeId, MediaKind) to avoid redundant work.
        // If a 'Remove' task exists, it supersedes all prior tasks for that (AnimeId, MediaKind).
        return pendingTasks
            .GroupBy(t => (t.AnimeId, Kind: ResolveEntityMediaKind(t)))
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

    public async Task EnqueueUpdateAsync(int animeId, MediaKind mediaKind, int progress, UserAnimeStatus? status = null, int? score = null, string? targetTracker = null)
    {
        var task = new SyncTask
        {
            AnimeId = animeId,
            MediaKind = mediaKind,
            Type = SyncTaskType.UpdateProgress,
            Progress = progress,
            Status = status,
            Score = score,
            TargetTracker = targetTracker
        };

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
        task.Id = await _syncTaskRepo.AddAsync(entity);

        RecordLatestTask(task);
        try
        {
            var pendingTrackers = !string.IsNullOrEmpty(targetTracker)
                ? new List<string> { targetTracker }
                : destinationTrackers;

            if (pendingTrackers.Count > 0)
            {
                _historyService.SetPendingTrackers(animeId, mediaKind, progress, pendingTrackers);
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
        var entity = MapToEntity(task);
        task.Id = await _syncTaskRepo.AddAsync(entity);

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
        int effectiveProgress = item.MediaKind != MediaKind.Anime ? item.ChaptersRead : item.Progress;
        var task = new SyncTask
        {
            AnimeId = item.Id,
            MediaKind = item.MediaKind,
            Type = SyncTaskType.FullUpdate,
            FullItem = item,
            Progress = effectiveProgress,
            Status = item.Status
        };
        var entity = MapToEntity(task);
        task.Id = await _syncTaskRepo.AddAsync(entity);

        RecordLatestTask(task);
        try
        {
            var destinationTrackers = GetDestinationTrackers().Select(t => t.Name).ToList();
            if (destinationTrackers.Count > 0)
            {
                _historyService.SetPendingTrackers(item.Id, item.MediaKind, effectiveProgress, destinationTrackers);
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

}
