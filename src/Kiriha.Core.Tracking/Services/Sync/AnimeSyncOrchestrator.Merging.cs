using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Sync;

public partial class AnimeSyncOrchestrator
{
    private async Task ProcessSyncResults(List<AnimeEntity> apiList, List<AnimeEntity> currentItems, IProgress<string>? status, CancellationToken ct)
    {
        var distinctApiList = apiList.DistinctBy(x => (x.Id, x.MediaKind)).ToList();

        var apiMap = new Dictionary<(int Id, MediaKind Kind), AnimeEntity>(distinctApiList.Count);
        for (int i = 0; i < distinctApiList.Count; i++) apiMap[(distinctApiList[i].Id, distinctApiList[i].MediaKind)] = distinctApiList[i];

        var existingMap = new Dictionary<(int Id, MediaKind Kind), AnimeEntity>(currentItems.Count);
        for (int i = 0; i < currentItems.Count; i++) existingMap[(currentItems[i].Id, currentItems[i].MediaKind)] = currentItems[i];

        var toRemove = new List<AnimeEntity>();
        for (int i = 0; i < currentItems.Count; i++)
        {
            if (!apiMap.ContainsKey((currentItems[i].Id, currentItems[i].MediaKind)))
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

            if (_animeRepository.IsRecentlyDeleted(newItem.Id, newItem.MediaKind)) continue;

            if (existingMap.TryGetValue((newItem.Id, newItem.MediaKind), out var existing))
            {
                var captured = newItem;
                var capturedExisting = existing;
                uiBatch.Add(() => captured.CopyTo(capturedExisting));
            }
            else
            {
                existingMap[(newItem.Id, newItem.MediaKind)] = newItem;
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

                status?.Report($"{"sync.updating.metadata"}: {i + 1}/{total}");
                await Task.Delay(1, ct);
            }
        }

        if (!ct.IsCancellationRequested && (uiBatch.Count > 0 || toRemove.Count > 0))
        {
            await _animeRepository.ApplySyncBatchAsync(toRemove, uiBatch);
        }
    }
}
