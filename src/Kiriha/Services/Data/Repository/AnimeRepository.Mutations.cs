using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Services.Data.Repository;

public partial class AnimeRepository
{
    private static readonly TimeSpan RecentDeleteExpiryDelay = TimeSpan.FromSeconds(60);
    public bool IsRecentlyDeleted(int animeId)
    {
        lock (_recentlyDeletedLock) return _recentlyDeletedIds.ContainsKey(animeId);
    }


    public async Task AddOrUpdateAnimeAsync(AnimeEntity item)
    {
        if (item.Id <= 0 || string.IsNullOrWhiteSpace(item.Title))
        {
            Log.Warning("AnimeRepository.AddOrUpdateAnimeAsync: Rejected invalid entity (Id: {Id}, Title: {Title})", item.Id, item.Title);
            return;
        }

        if (item.Status == UserAnimeStatus.None)
        {
            Log.Warning("AnimeRepository.AddOrUpdateAnimeAsync: Item has Status None (Id: {Id}). Ensuring it is removed from list.", item.Id);
            await RemoveAnimeLocalAsync(item.Id);
            return;
        }

        lock (_recentlyDeletedLock)
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
                if (!ReferenceEquals(item, found))
                {
                    item.PreserveUserFieldsFrom(found);
                    item.CopyTo(found);
                }
            }
            else
            {
                Collection.Add(item);
                _idIndex[item.Id] = item;
            }
            return found;
        });

        if (existing == null)
        {
            var dbExisting = await _userAnimeRepo.GetByIdAsync(item.Id);
            if (dbExisting != null)
            {
                item.PreserveUserFieldsFrom(dbExisting);
            }
        }

        await _userAnimeRepo.UpdateAsync(existing ?? item);
    }

    public async Task RemoveAnimeLocalAsync(int animeId)
    {
        var newCts = new CancellationTokenSource();
        lock (_recentlyDeletedLock)
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
                await Task.Delay(RecentDeleteExpiryDelay, linkedCts.Token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                lock (_recentlyDeletedLock)
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
