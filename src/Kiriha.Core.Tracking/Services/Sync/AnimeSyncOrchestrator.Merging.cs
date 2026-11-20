using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Sync.Models;

namespace Kiriha.Core.Tracking.Sync;

public partial class AnimeSyncOrchestrator
{
    private async Task ProcessSyncResults(
        List<AnimeEntity> apiList,
        List<AnimeEntity> currentItems,
        List<SyncTaskEntity> pendingTasks,
        List<SyncTaskEntity> pendingAtFetchStart,
        HashSet<(int Id, MediaKind Kind)> protectedItems,
        IProgress<string>? status,
        CancellationToken ct)
    {
        var pendingRemoves = new HashSet<(int Id, MediaKind Kind)>();
        // A task pending at request time may be acknowledged before the stale list arrives.
        foreach (var task in GetLatestTasksByTitle(pendingAtFetchStart))
        {
            if (!IsRemove(task))
                protectedItems.Add((task.AnimeId, task.MediaKind));
        }

        foreach (var task in GetLatestTasksByTitle(pendingTasks))
        {
            var key = (task.AnimeId, task.MediaKind);
            if (IsRemove(task))
            {
                pendingRemoves.Add(key);
                protectedItems.Remove(key);
            }
            else
                protectedItems.Add(key);
        }

        var distinctApiList = apiList
            .Where(x => !pendingRemoves.Contains((x.Id, x.MediaKind)))
            .DistinctBy(x => (x.Id, x.MediaKind))
            .ToList();

        var apiMap = new Dictionary<(int Id, MediaKind Kind), AnimeEntity>(distinctApiList.Count);
        for (int i = 0; i < distinctApiList.Count; i++) apiMap[(distinctApiList[i].Id, distinctApiList[i].MediaKind)] = distinctApiList[i];

        var existingMap = new Dictionary<(int Id, MediaKind Kind), AnimeEntity>(currentItems.Count);
        for (int i = 0; i < currentItems.Count; i++) existingMap[(currentItems[i].Id, currentItems[i].MediaKind)] = currentItems[i];

        var toRemove = new List<AnimeEntity>();
        for (int i = 0; i < currentItems.Count; i++)
        {
            var key = (currentItems[i].Id, currentItems[i].MediaKind);
            if (!apiMap.ContainsKey(key) && !protectedItems.Contains(key))
            {
                toRemove.Add(currentItems[i]);
            }
        }

        var uiBatch = new List<Action>(50);
        int total = distinctApiList.Count;

        for (int i = 0; i < total; i++)
        {
            if (ct.IsCancellationRequested) break;

            var newItem = distinctApiList[i];
            var key = (newItem.Id, newItem.MediaKind);

            if (_animeRepository.IsRecentlyDeleted(newItem.Id, newItem.MediaKind)) continue;

            if (existingMap.TryGetValue(key, out var existing))
            {
                var captured = newItem;
                var capturedExisting = existing;
                bool preserveLocalEdits = protectedItems.Contains(key);
                uiBatch.Add(() =>
                {
                    if (preserveLocalEdits)
                        capturedExisting.MergeFullDetails(captured);
                    else
                        captured.CopyTo(capturedExisting);
                });
            }
            else
            {
                existingMap[key] = newItem;
                uiBatch.Add(() =>
                {
                    _animeRepository.AddToCollection(newItem);
                });
            }

            if (uiBatch.Count >= 50)
            {
                var currentBatch = uiBatch.ToList();
                uiBatch.Clear();
                await _animeRepository.ApplySyncBatchAsync(new List<AnimeEntity>(), currentBatch);

                status?.Report($"{FormatLoc("sync.updating.metadata")}: {i + 1}/{total}");
                await Task.Delay(1, ct);
            }
        }

        if (!ct.IsCancellationRequested && (uiBatch.Count > 0 || toRemove.Count > 0))
        {
            await _animeRepository.ApplySyncBatchAsync(toRemove, uiBatch);
        }
    }

    private static IEnumerable<SyncTaskEntity> GetLatestTasksByTitle(List<SyncTaskEntity> tasks) =>
        tasks.GroupBy(task => (task.AnimeId, task.MediaKind))
            .Select(group => group.OrderBy(task => task.Id).Last());

    private static bool IsRemove(SyncTaskEntity task) =>
        string.Equals(task.Type, nameof(SyncTaskType.Remove), StringComparison.OrdinalIgnoreCase);
}
