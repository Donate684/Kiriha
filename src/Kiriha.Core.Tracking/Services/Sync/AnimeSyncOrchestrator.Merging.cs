using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Sync;

public partial class AnimeSyncOrchestrator
{
    private async Task ProcessSyncResults(List<AnimeEntity> apiList, List<AnimeEntity> currentItems, IProgress<string>? status, CancellationToken ct)
    {
        var distinctApiList = apiList.DistinctBy(x => x.Id).ToList();

        var apiMap = new Dictionary<int, AnimeEntity>(distinctApiList.Count);
        for (int i = 0; i < distinctApiList.Count; i++) apiMap[distinctApiList[i].Id] = distinctApiList[i];

        var existingMap = new Dictionary<int, AnimeEntity>(currentItems.Count);
        for (int i = 0; i < currentItems.Count; i++) existingMap[currentItems[i].Id] = currentItems[i];

        var toRemove = new List<AnimeEntity>();
        for (int i = 0; i < currentItems.Count; i++)
        {
            if (!apiMap.ContainsKey(currentItems[i].Id))
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

            if (_animeRepository.IsRecentlyDeleted(newItem.Id)) continue;

            if (existingMap.TryGetValue(newItem.Id, out var existing))
            {
                var captured = newItem;
                var capturedExisting = existing;
                uiBatch.Add(() => captured.CopyTo(capturedExisting));
            }
            else
            {
                existingMap[newItem.Id] = newItem;
                uiBatch.Add(() =>
                {
                    _animeRepository.AddToCollection(newItem);
                });
            }

            if (uiBatch.Count >= 50 || i == total - 1)
            {
                if (uiBatch.Count > 0)
                {
                    var currentBatch = uiBatch.ToList();
                    uiBatch.Clear();
                    var removeList = (i == total - 1) ? toRemove : new List<AnimeEntity>();
                    await _animeRepository.ApplySyncBatchAsync(removeList, currentBatch);
                }

                status?.Report($"{"sync.updating.metadata"}: {i + 1}/{total}");
                if (i < total - 1)
                {
                    await Task.Delay(1, ct);
                }
            }
        }

        // If total == 0, still remove
        if (total == 0 && toRemove.Any())
        {
            await _animeRepository.ApplySyncBatchAsync(toRemove, uiBatch);
        }
    }
}
