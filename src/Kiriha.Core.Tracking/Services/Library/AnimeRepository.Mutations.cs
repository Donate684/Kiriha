using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Library;

public partial class AnimeRepository
{
    private static readonly TimeSpan RecentDeleteExpiryDelay = TimeSpan.FromSeconds(60);

    public bool IsRecentlyDeleted(int animeId, MediaKind kind)
    {
        lock (_recentlyDeletedLock) return _recentlyDeletedIds.ContainsKey((animeId, kind));
    }


    public async Task AddOrUpdateAnimeAsync(AnimeEntity anime)
    {
        if (anime.Id <= 0 || string.IsNullOrWhiteSpace(anime.Title))
        {
            Log.Warning("AnimeRepository.AddOrUpdateAnimeAsync: Rejected invalid entity (Id: {Id}, Title: {Title})", anime.Id, anime.Title);
            return;
        }

        if (anime.Status == UserAnimeStatus.None)
        {
            Log.Warning("AnimeRepository.AddOrUpdateAnimeAsync: Item has Status None (Id: {Id}). Ensuring it is removed from list.", anime.Id);
            await RemoveAnimeLocalAsync(anime.Id, anime.MediaKind);
            return;
        }

        var key = (anime.Id, anime.MediaKind);
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
                if (!ReferenceEquals(anime, found))
                {
                    anime.CopyTo(found);
                }
            }
            else
            {
                Collection.Add(anime);
                lock (_indexLock)
                {
                    _idIndex[key] = anime;
                }
            }
            return found;
        });

        if (existing != null)
            await _userAnimeRepo.UpdateAsync(existing);
        else
            await _userAnimeRepo.UpsertAsync(anime);
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
