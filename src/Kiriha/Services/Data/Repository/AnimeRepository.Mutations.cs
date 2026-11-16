using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Services.Data.Repository;

public partial class AnimeRepository
{
    private static readonly TimeSpan RecentDeleteExpiryDelay = TimeSpan.FromSeconds(60);
    public bool IsRecentlyDeleted(int animeId)
    {
        lock (_recentlyDeletedLock) return _recentlyDeletedIds.Keys.Any(k => k.Id == animeId);
    }

    public bool IsRecentlyDeleted(int animeId, MediaKind kind)
    {
        lock (_recentlyDeletedLock) return _recentlyDeletedIds.ContainsKey((animeId, kind));
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
            await RemoveAnimeLocalAsync(item.Id, item.MediaKind);
            return;
        }

        var key = (item.Id, item.MediaKind);
        lock (_recentlyDeletedLock)
        {
            if (_recentlyDeletedIds.TryGetValue(key, out var cts))
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                _recentlyDeletedIds.Remove(key);
            }
        }

        var existing = await _uiDispatcher.InvokeAsync(() =>
        {
            AnimeEntity? found;
            lock (_indexLock)
            {
                _idIndex.TryGetValue(key, out found);
            }

            if (found != null)
            {
                if (!ReferenceEquals(item, found))
                {
                    item.CopyTo(found);
                }
            }
            else
            {
                Collection.Add(item);
                lock (_indexLock)
                {
                    _idIndex[key] = item;
                }
            }
            return found;
        });

        if (existing != null)
            await _userAnimeRepo.UpdateAsync(existing);
        else
            await _userAnimeRepo.UpsertAsync(item);
    }

    public Task RemoveAnimeLocalAsync(int animeId) => RemoveAnimeLocalAsync(animeId, MediaKind.Anime);

    public async Task RemoveAnimeLocalAsync(int animeId, MediaKind kind)
    {
        var key = (animeId, kind);
        var newCts = new CancellationTokenSource();
        lock (_recentlyDeletedLock)
        {
            if (_recentlyDeletedIds.TryGetValue(key, out var oldCts))
            {
                try { oldCts.Cancel(); } catch (ObjectDisposedException) { }
            }
            _recentlyDeletedIds[key] = newCts;
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
                    if (_recentlyDeletedIds.TryGetValue(key, out var currentCts) && currentCts == newCts)
                    {
                        _recentlyDeletedIds.Remove(key);
                    }
                }
                newCts.Dispose();
            }
        });

        await _uiDispatcher.InvokeAsync(() =>
        {
            AnimeEntity? item;
            lock (_indexLock)
            {
                if (_idIndex.TryGetValue(key, out item))
                {
                    _idIndex.Remove(key);
                }
            }
            if (item != null)
            {
                Collection.Remove(item);
            }
        });

        await _userAnimeRepo.DeleteAsync(animeId, kind);
    }
}
