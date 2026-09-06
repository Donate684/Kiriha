using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Infrastructure;
using Kiriha.Core.Abstractions.Infrastructure;

using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Tracking.Sync;

public partial class AnimeSyncOrchestrator
{
    private async Task ProcessSyncResults(List<AnimeEntity> apiList, List<AnimeEntity> currentItems, IProgress<string>? status, CancellationToken ct)
    {
        var apiMap = new Dictionary<int, AnimeEntity>(apiList.Count);
        for (int i = 0; i < apiList.Count; i++) apiMap[apiList[i].Id] = apiList[i];

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
        int total = apiList.Count;

        for (int i = 0; i < total; i++)
        {
            if (ct.IsCancellationRequested) break;

            var newItem = apiList[i];

            if (_animeRepository.IsRecentlyDeleted(newItem.Id)) continue;

            if (existingMap.TryGetValue(newItem.Id, out var existing))
            {
                var captured = newItem;
                var capturedExisting = existing;
                uiBatch.Add(() => captured.CopyTo(capturedExisting));
            }
            else
            {
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
