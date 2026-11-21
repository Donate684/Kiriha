using Kiriha.Core.Domain.Utils.Parsing;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Utils.Async;
using Kiriha.Infrastructure.Caching;
using Serilog;

namespace Kiriha.ViewModels.Seasonal;

public partial class SeasonalViewModel
{
    private void HydrateDiskCache()
    {
        try
        {
            foreach (var entry in _cacheStore.LoadAll())
                _seasonalCache.TryAdd((entry.Year, entry.Season), (entry.Items, entry.ValidatedAt));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SeasonalViewModel: disk cache hydration failed");
        }
    }

    private void ScheduleDeferredInitialLoad()
    {
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
                if (_isDisposed) return;
                Dispatcher.UIThread.Post(() => EnsureInitialLoad());
            }
            catch
            {
                // fire-and-forget
            }
        }, CancellationToken.None);
    }

    private void EnsureInitialLoad()
    {
        if (_isDisposed) return;
        if (Interlocked.CompareExchange(ref _initialLoadStarted, 1, 0) != 0) return;
        LoadSeasonalAnimeAsync().SafeFireAndForget("LoadSeasonalAnimeAsync");
    }

    public Task OnOpenedAsync()
    {
        if (_isDisposed || IsLoading) return Task.CompletedTask;
        if (Interlocked.CompareExchange(ref _initialLoadStarted, 1, 0) == 0)
            return LoadSeasonalAnimeAsync();

        var key = (CurrentYear, CurrentSeason);
        var now = _clock.GetUtcNow().UtcDateTime;
        if (HasFreshSnapshot(key, now) || _seasonalRetryAfter.GetValueOrDefault(key) > now)
            return Task.CompletedTask;

        return LoadSeasonalAnimeAsync();
    }

    public void InvalidateCache()
    {
        _seasonalCache.Clear();
        _seasonalRetryAfter.Clear();
        SetAllSeasonalItems(new List<AnimeEntity>());
        DisplayItems = new AvaloniaList<AnimeEntity>();

        if (!_isDisposed && Volatile.Read(ref _initialLoadStarted) != 0)
            LoadSeasonalAnimeAsync().SafeFireAndForget("LoadSeasonalAnimeAsync");
    }

    [RelayCommand]
    public Task LoadSeasonalAnimeAsync() => LoadSeasonalAnimeInternalAsync(force: false);

    [RelayCommand]
    public Task ForceRefreshAsync() => LoadSeasonalAnimeInternalAsync(force: true);

    private async Task LoadSeasonalAnimeInternalAsync(bool force)
    {
        if (_isDisposed) return;

        var newCts = new CancellationTokenSource();
        var ct = newCts.Token;
        var capturedYear = CurrentYear;
        var capturedSeason = CurrentSeason;
        var oldCts = Interlocked.Exchange(ref _loadCts, newCts);

        if (oldCts != null)
        {
            try { await oldCts.CancelAsync(); } catch (ObjectDisposedException) { }
            oldCts.Dispose();
        }

        if (ct.IsCancellationRequested) return;
        var cacheKey = (capturedYear, capturedSeason);
        var cached = _seasonalCache.TryGetValue(cacheKey, out var snapshot) ? snapshot.Items : null;

        if (cached is null || force) IsLoading = true;

        try
        {
            if (cached is not null && !force)
            {
                SetAllSeasonalItems(cached);
                var changed = await HydrateMetadataAsync(cached, ct);
                changed |= await HydrateCountryOriginAsync(cached, ct);
                if (ct.IsCancellationRequested) return;
                if (changed)
                    await _cacheStore.SaveAsync(capturedYear, capturedSeason, cached, catalogValidatedAt: snapshot.ValidatedAt);
                if (ct.IsCancellationRequested) return;
                await ApplyFiltersAsync();

                if (!ct.IsCancellationRequested)
                    await RefreshSeasonalCacheInBackground(capturedYear, capturedSeason, ct);
            }
            else
            {
                SetAllSeasonalItems(cached ?? []);
                await ApplyFiltersAsync();
                if (ct.IsCancellationRequested) return;

                var fresh = await RefreshSeasonAsync(capturedYear, capturedSeason, force, ct);
                if (fresh is not null && !ct.IsCancellationRequested)
                {
                    SetAllSeasonalItems(fresh);
                    await ApplyFiltersAsync();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load seasonal anime");
        }
        finally
        {
            if (Volatile.Read(ref _loadCts) == newCts)
            {
                IsLoading = false;
            }
        }
    }

    private async Task<bool> HydrateCountryOriginAsync(IReadOnlyList<AnimeEntity> items, CancellationToken ct)
    {
        if (items.Count == 0 || ct.IsCancellationRequested) return false;

        try
        {
            return await _countryService.HydrateCountriesAsync(items, ct);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SeasonalViewModel: HydrateCountryOriginAsync failed");
            return false;
        }
    }

    private async Task<bool> HydrateMetadataAsync(IReadOnlyList<AnimeEntity> items, CancellationToken ct)
    {
        if (items.Count == 0 || ct.IsCancellationRequested) return false;
        var missingKeys = items
            .Where(x => string.IsNullOrEmpty(x.RussianTitle) || string.IsNullOrEmpty(x.RussianSynopsis))
            .Select(x => new MediaKey(x.Id, MediaKind.Anime))
            .ToList();

        if (missingKeys.Count == 0) return false;

        try
        {
            var metaDict = await _metadataRepo.GetBatchAsync(missingKeys, ct);
            if (metaDict is null || metaDict.Count == 0 || ct.IsCancellationRequested) return false;

            bool updatedAny = false;
            foreach (var item in items)
            {
                if (metaDict.TryGetValue(new MediaKey(item.Id, MediaKind.Anime), out var meta))
                {
                    if (string.IsNullOrEmpty(item.RussianTitle) && !string.IsNullOrEmpty(meta.Russian))
                    {
                        item.RussianTitle = meta.Russian;
                        updatedAny = true;
                    }
                    if (string.IsNullOrEmpty(item.RussianSynopsis) && !string.IsNullOrEmpty(meta.Description))
                    {
                        item.RussianSynopsis = Kiriha.Core.Domain.Utils.Parsing.AnimeStringHelper.CleanShikiDescription(meta.Description);
                        updatedAny = true;
                    }
                }
            }

            return updatedAny;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SeasonalViewModel: HydrateMetadataAsync failed");
            return false;
        }
    }

    private async Task RefreshSeasonalCacheInBackground(int year, string season, CancellationToken ct)
    {
        try
        {
            var fresh = await RefreshSeasonAsync(year, season, force: false, ct);
            if (fresh is not null && !ct.IsCancellationRequested && !_isDisposed && year == CurrentYear && season == CurrentSeason)
            {
                SetAllSeasonalItems(fresh);
                await ApplyFiltersAsync();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Debug(ex, "Background seasonal refresh failed");
        }
    }

    private async Task<List<AnimeEntity>?> RefreshSeasonAsync(int year, string season, bool force, CancellationToken ct)
    {
        var key = (year, season);
        var now = _clock.GetUtcNow().UtcDateTime;
        if (!force && HasFreshSnapshot(key, now))
            return null;

        var retryAfter = now + SeasonalRetryDelay;
        if (force)
            _seasonalRetryAfter[key] = retryAfter;
        else if (!_seasonalRetryAfter.TryAdd(key, retryAfter) &&
            (!_seasonalRetryAfter.TryGetValue(key, out var nextRetry) ||
             nextRetry > now || !_seasonalRetryAfter.TryUpdate(key, retryAfter, nextRetry)))
            return null;

        try
        {
            var fresh = await _apiService.GetSeasonalAnimeAsync(year, season, ct);
            if (ct.IsCancellationRequested || fresh is not { Count: > 0 }) return null;

            await HydrateMetadataAsync(fresh, ct);
            await HydrateCountryOriginAsync(fresh, ct);
            if (ct.IsCancellationRequested) return null;

            var validatedAt = _clock.GetUtcNow().UtcDateTime;
            _seasonalCache[key] = (fresh, validatedAt);
            _seasonalRetryAfter.TryRemove(new KeyValuePair<(int, string), DateTime>(key, retryAfter));
            await _cacheStore.SaveAsync(year, season, fresh, validated: true, catalogValidatedAt: validatedAt);
            return fresh;
        }
        finally
        {
            if (ct.IsCancellationRequested)
                _seasonalRetryAfter.TryRemove(new KeyValuePair<(int, string), DateTime>(key, retryAfter));
        }
    }

    private bool HasFreshSnapshot((int Year, string Season) key, DateTime now) =>
        _seasonalCache.TryGetValue(key, out var snapshot) &&
        now - snapshot.ValidatedAt < SeasonalCacheStore.GetFreshness(key.Year, key.Season, now);

    private void SetAllSeasonalItems(List<AnimeEntity> items)
    {
        DetachItemListeners(_allSeasonalItems);
        _allSeasonalItems = items;
        AttachItemListeners(_allSeasonalItems);
    }

    private void AttachItemListeners(IEnumerable<AnimeEntity>? items)
    {
        if (items is null) return;
        foreach (var item in items)
        {
            item.PropertyChanged += OnSeasonalItemPropertyChanged;
        }
    }

    private void DetachItemListeners(IEnumerable<AnimeEntity>? items)
    {
        if (items is null) return;
        foreach (var item in items)
        {
            item.PropertyChanged -= OnSeasonalItemPropertyChanged;
        }
    }

    private void OnSeasonalItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if ((e.PropertyName == nameof(AnimeEntity.RussianTitle) && IsRussianTitleSort) ||
            (e.PropertyName == nameof(AnimeEntity.MeanScore) && IsScoreSort) ||
            (e.PropertyName == nameof(AnimeEntity.Popularity) && IsPopularitySort) ||
            (e.PropertyName == nameof(AnimeEntity.AiringDate) && IsDateSort))
        {
            ApplyFilters();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        var cts = Interlocked.Exchange(ref _loadCts, null);
        if (cts != null)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }

        DetachItemListeners(_allSeasonalItems);
        _franchiseService.IndexRebuilt -= OnFranchiseIndexRebuilt;
        _filterDebouncer?.Dispose();
        _applyFilterDebouncer?.Dispose();
    }
}
