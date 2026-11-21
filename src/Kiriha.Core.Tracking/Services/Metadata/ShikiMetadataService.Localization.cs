using Kiriha.Core.Domain.Utils.Parsing;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Tracking.Api;
using Serilog;

namespace Kiriha.Core.Tracking.Metadata;

public partial class ShikiMetadataService
{
    public async Task LocalizeItemsAsync(IEnumerable<AnimeEntity> items, Action<int>? onProgress = null, CancellationToken ct = default)
    {
        bool useRussian = _settingsService.Current.UI.UseRussianTitles || _settingsService.Current.UI.UseRussianDescriptions;
        if (!useRussian)
            return;

        var itemList = items as IReadOnlyList<AnimeEntity> ?? items.ToList();
        if (itemList.Count == 0)
            return;

        int localizedCount = 0;
        var uncached = new List<AnimeEntity>();

        var cacheKeys = itemList.Select(i => new MediaKey(i.Id, i.MediaKind)).Distinct();
        var cachedMap = await _metadataRepo.GetBatchAsync(cacheKeys, ct);

        foreach (var item in itemList)
        {
            var cacheKey = new MediaKey(item.Id, item.MediaKind);
            var meta = cachedMap != null
                ? cachedMap.GetValueOrDefault(cacheKey)
                : await _metadataRepo.GetAsync(item.Id, item.MediaKind, ct);
            if (meta != null)
            {
                // ApplyMetadata mutates ObservableObject properties — dispatch to UI thread.
                bool changed = await _uiDispatcher.InvokeAsync(() => ApplyMetadata(item, meta));
                if (changed) await _userAnimeRepo.UpdateMetadataAsync(item, ct);
                localizedCount++;
            }
            else
            {
                uncached.Add(item);
            }
        }

        onProgress?.Invoke(localizedCount);

        if (uncached.Count > 0)
        {
            var tasks = new List<Task>();
            foreach (var item in uncached)
            {
                var cacheKey = new MediaKey(item.Id, item.MediaKind);
                if (!_activeFetches.TryAdd(cacheKey, 0)) continue;

                tasks.Add(Task.Run(async () =>
                {
                    bool acquired = false;
                    try
                    {
                        await _concurrentFetches.WaitAsync(ct);
                        acquired = true;
                        var fetchedMeta = await GetOrFetchMetadataAsync(item.Id, mediaKind: item.MediaKind).WaitAsync(ct);
                        if (fetchedMeta != null)
                        {
                            bool changed = await _uiDispatcher.InvokeAsync(() => ApplyMetadata(item, fetchedMeta));
                            if (changed) await _userAnimeRepo.UpdateMetadataAsync(item, ct);
                            int count = System.Threading.Interlocked.Increment(ref localizedCount);
                            onProgress?.Invoke(count);
                        }
                    }
                    finally
                    {
                        if (acquired)
                            _concurrentFetches.Release();
                        _activeFetches.TryRemove(cacheKey, out _);
                    }
                }, CancellationToken.None));
            }
            await Task.WhenAll(tasks);
        }
    }

    /// <summary>
    /// Ensure <paramref name="item"/>'s Russian title/synopsis are populated
    /// from the Shikimori metadata cache, fetching from the API on miss.
    /// Mirrors the per-item path inside <see cref="LocalizeItemsAsync"/> so
    /// callers (e.g. NowPlaying) get the same write-through-and-persist
    /// behaviour without rolling their own apply logic — bare
    /// <c>AnimeEntity.RefreshMetadata()</c> only raises PropertyChanged, it
    /// does NOT copy fetched fields into the item.
    /// </summary>
    public async Task EnsureLocalizedAsync(AnimeEntity item, CancellationToken ct = default)
    {
        if (item is null) return;
        bool useRussian = _settingsService.Current.UI.UseRussianTitles || _settingsService.Current.UI.UseRussianDescriptions;
        bool missingEpisodes = item.MediaKind == MediaKind.Anime && item.TotalEpisodes <= 0;
        if (!useRussian && !missingEpisodes) return;
        if (!missingEpisodes && !string.IsNullOrEmpty(item.RussianTitle) && !string.IsNullOrEmpty(item.RussianSynopsis)) return;

        bool missingData = missingEpisodes ||
                           (string.IsNullOrEmpty(item.RussianTitle) && _settingsService.Current.UI.UseRussianTitles) ||
                           (string.IsNullOrEmpty(item.RussianSynopsis) && _settingsService.Current.UI.UseRussianDescriptions);
        TimeSpan? maxAge = missingData ? TimeSpan.FromHours(12) : null;

        var key = new MediaKey(item.Id, item.MediaKind);
        var generation = _library?.GetEntryGeneration(key.Id, key.Kind);
        var meta = await GetOrFetchMetadataAsync(key.Id, maxAge: maxAge, onFetched: null, key.Kind);
        ct.ThrowIfCancellationRequested();
        if (meta is null) return;

        using var write = _syncGate != null ? await _syncGate.AcquireProgressWriteAsync(ct) : null;
        if (_library?.GetEntryGeneration(key.Id, key.Kind) != generation) return;

        var current = _library == null ? item : _library.FindById(key.Id, key.Kind);
        if (current != null)
        {
            var draft = await _uiDispatcher.InvokeAsync(() => current.Clone());
            if (ApplyMetadata(draft, meta))
            {
                try
                {
                    await _userAnimeRepo.UpdateMetadataAsync(draft, ct);
                    await _uiDispatcher.InvokeAsync(() => ApplyMetadata(current, meta));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Debug(ex, "EnsureLocalizedAsync: failed to persist localized fields for {Id}", key.Id);
                }
            }
        }

        if (!ReferenceEquals(item, current))
            await _uiDispatcher.InvokeAsync(() => ApplyMetadata(item, meta));
    }

    private bool ApplyMetadata(AnimeEntity item, ShikiMetadata meta)
    {
        bool changed = false;

        if (!string.IsNullOrEmpty(meta.Name))
        {
            if (string.IsNullOrEmpty(item.Title) ||
                item.Title.StartsWith("ID ", StringComparison.Ordinal) ||
                (!string.IsNullOrEmpty(item.EnglishTitle) && string.Equals(item.Title.Trim(), item.EnglishTitle.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                if (item.Title != meta.Name)
                {
                    item.Title = meta.Name;
                    changed = true;
                }
            }
        }

        if (_settingsService.Current.UI.UseRussianTitles && !string.IsNullOrEmpty(meta.Russian))
        {
            if (item.RussianTitle != meta.Russian) { item.RussianTitle = meta.Russian; changed = true; }
        }

        if (_settingsService.Current.UI.UseRussianDescriptions && !string.IsNullOrEmpty(meta.Description))
        {
            var cleaned = Kiriha.Core.Domain.Utils.Parsing.AnimeStringHelper.CleanShikiDescription(meta.Description);
            if (item.RussianSynopsis != cleaned) { item.RussianSynopsis = cleaned; changed = true; }
        }

        if (!string.IsNullOrEmpty(meta.PosterUrl) && (string.IsNullOrEmpty(item.MainPictureUrl) || AnimeEntity.IsMissingPosterUrl(item.MainPictureUrl)))
        {
            item.MainPictureUrl = meta.PosterUrl;
            changed = true;
        }

        if (item.MediaKind == MediaKind.Anime && item.TotalEpisodes <= 0 && meta.Episodes is > 0)
        {
            item.TotalEpisodes = meta.Episodes.Value;
            changed = true;
        }

        if (meta.Studios != null && meta.Studios.Count > 0 && item.Studios.Count == 0)
        {
            item.Studios.AddRange(meta.Studios);
            changed = true;
        }

        if (meta.Genres != null && meta.Genres.Count > 0 && item.Genres.Count == 0)
        {
            item.Genres.AddRange(meta.Genres);
            changed = true;
        }

        // EpisodesAired and NextEpisodeAt are intentionally NOT applied from Shikimori.
        // Shiki's `episodes_aired` is known to lead `next_episode_at` by one (e.g. claims
        // 5 aired while next_episode_at still points at episode 5 in the future), which
        // used to inflate our counter. Airing state is owned by AniList's
        // nextAiringEpisode in AiringInfoService.

        if (changed) item.RefreshMetadata();
        return changed;
    }

    public async Task<string?> ResolveRussianQueryAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var url = $"{ShikiBaseUrl}animes?search={Uri.EscapeDataString(query)}&limit=1";
            var result = await _httpCache.SendForResultAsync(
                url,
                requestFactory: innerCt =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("User-Agent", AppInfo.UserAgent);
                    return Task.FromResult(request);
                },
                throttle: ct => _rateLimiter.ThrottleAsync(ct),
                ct: ct,
                getFreshness: CatalogSearchCachePolicy.Shiki);

            if (result.Body is null) return null;

            var list = System.Text.Json.JsonSerializer.Deserialize<List<ShikiMetadata>>(result.Body);
            if (list != null && list.Count > 0)
                return list[0].Name;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to resolve Russian query on Shikimori");
        }
        return null;
    }
}
