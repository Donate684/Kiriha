using System;
using Kiriha.Services.Data.Mapping;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Kiriha.Core.Abstractions.Models.Entities;
using System.Linq;

namespace Kiriha.Services.Data.Repository;

public partial class AnimeRepository
{
    public bool IsRecentlyDeleted(int animeId)
    {
        lock (_recentlyDeletedIds) return _recentlyDeletedIds.ContainsKey(animeId);
    }


    public async Task AddOrUpdateAnimeAsync(AnimeEntity item)
    {
        lock (_recentlyDeletedIds)
        {
            if (_recentlyDeletedIds.TryGetValue(item.Id, out var cts))
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                _recentlyDeletedIds.Remove(item.Id);
            }
        }

        var existing = await _uiDispatcher.InvokeAsync(() =>
        {
            _idIndex.TryGetValue(item.Id, out var found);
            if (found != null)
            {
                item.CopyTo(found);
            }
            else
            {
                Collection.Add(item);
                _idIndex[item.Id] = item;
            }
            return found;
        });

        await _userAnimeRepo.UpdateAsync(item);
    }

    public async Task RemoveAnimeLocalAsync(int animeId)
    {
        var newCts = new CancellationTokenSource();
        lock (_recentlyDeletedIds)
        {
            if (_recentlyDeletedIds.TryGetValue(animeId, out var oldCts))
            {
                try { oldCts.Cancel(); } catch (ObjectDisposedException) { }
            }
            _recentlyDeletedIds[animeId] = newCts;
        }

        _ = _backgroundTasks.Run("AnimeRepository.RecentDeleteExpiry", async ct =>
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, newCts.Token);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), linkedCts.Token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                lock (_recentlyDeletedIds)
                {
                    if (_recentlyDeletedIds.TryGetValue(animeId, out var currentCts) && currentCts == newCts)
                    {
                        _recentlyDeletedIds.Remove(animeId);
                    }
                }
                newCts.Dispose();
            }
        });

        await _uiDispatcher.InvokeAsync(() =>
        {
            if (_idIndex.TryGetValue(animeId, out var item))
            {
                Collection.Remove(item);
                _idIndex.Remove(animeId);
            }
        });

        await _userAnimeRepo.DeleteAsync(animeId);
    }
}
