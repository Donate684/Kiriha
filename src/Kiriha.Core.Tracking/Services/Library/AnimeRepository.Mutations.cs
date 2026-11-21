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

        var existing = await PublishSavedEntryAsync(anime);
        if (existing != null)
            await _userAnimeRepo.UpdateAsync(existing);
        else
            await _userAnimeRepo.UpsertAsync(anime);
    }

    public async Task<(AnimeEditFields Fields, long Generation)?> RefreshEditorDetailsAsync(
        AnimeEntity original, AnimeEntity details, long generation, CancellationToken ct = default)
    {
        if (original.Id != details.Id ||
            (original.MediaKind == MediaKind.Anime) != (details.MediaKind == MediaKind.Anime))
            return null;

        using var write = _syncGate != null ? await _syncGate.AcquireProgressWriteAsync(ct) : null;
        var id = original.Id;
        var kind = original.MediaKind;
        if (GetEntryGeneration(id, kind) != generation)
            return null;

        var current = FindById(id, kind);
        if (current != null)
        {
            if (await _userAnimeRepo.MergeDetailsMetadataAsync(id, kind, details, ct))
                await _uiDispatcher.InvokeAsync(() => current.MergeFullDetails(details, metadataOnly: true));
            return null;
        }

        // A details lookup may discover an entry before its first list sync. A
        // title already removed this session must await normal list reconciliation.
        if (generation != 0 || details.Status == UserAnimeStatus.None || IsRecentlyDeleted(id, kind) ||
            await _userAnimeRepo.GetByIdAsync(id, kind, ct) != null)
            return null;

        var imported = original.Clone();
        imported.MergeFullDetails(details, metadataOnly: true);
        AnimeEditFields.From(details).ApplyTo(imported);
        await _userAnimeRepo.UpsertAsync(imported, ct);
        _syncGate?.RecordProgressChange(id, kind);
        await PublishSavedEntryAsync(original, imported);
        return (AnimeEditFields.From(imported), GetEntryGeneration(id, kind));
    }

    internal Task<AnimeEntity?> PublishSavedEntryAsync(AnimeEntity anime, AnimeEntity? committedEntry = null)
    {
        var key = (anime.Id, anime.MediaKind);
        lock (_recentlyDeletedLock)
        {
            if (_recentlyDeletedIds.TryGetValue(key, out var cts))
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                _recentlyDeletedIds.Remove(key);
            }
        }

        return _uiDispatcher.InvokeAsync(() =>
        {
            committedEntry?.CopyTo(anime);
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

    }

    public Task RemoveAnimeLocalAsync(int animeId) => RemoveAnimeLocalAsync(animeId, MediaKind.Anime);

    public async Task RemoveAnimeLocalAsync(int animeId, MediaKind kind)
    {
        await PublishRemovedEntryAsync(animeId, kind);
        await _userAnimeRepo.DeleteAsync(animeId, kind);
    }

    internal async Task PublishRemovedEntryAsync(int animeId, MediaKind kind)
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
                if (!_idIndex.TryGetValue(key, out item))
                    _entryGenerations[key] = ++_nextEntryGeneration;
            }
            if (item != null)
            {
                Collection.Remove(item);
            }
        });
    }
}
