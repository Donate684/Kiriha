using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Utils.Async;
using Serilog;

namespace Kiriha.ViewModels.Seasonal;

public partial class SeasonalViewModel
{
    private void HydrateDiskCacheOnce()
    {
        if (Interlocked.CompareExchange(ref _diskHydrated, 1, 0) != 0) return;

        try
        {
            foreach (var entry in _cacheStore.LoadAll())
            {
                _seasonalCache.TryAdd((entry.Year, entry.Season), entry.Items);
            }
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
                await Task.Delay(TimeSpan.FromSeconds(2));
                if (_isDisposed) return;
                Dispatcher.UIThread.Post(() => EnsureInitialLoad());
            }
            catch
            {
                // fire-and-forget
            }
        });
    }

    public void EnsureInitialLoad()
    {
        if (_isDisposed) return;
        if (Interlocked.CompareExchange(ref _initialLoadStarted, 1, 0) != 0) return;
        LoadSeasonalAnimeAsync().SafeFireAndForget("LoadSeasonalAnimeAsync");
    }

    public void InvalidateCache()
    {
        _seasonalCache.Clear();
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
        var oldCts = Interlocked.Exchange(ref _loadCts, newCts);

        if (oldCts != null)
        {
            try { oldCts.Cancel(); } catch (ObjectDisposedException) { }
            oldCts.Dispose();
        }

        var ct = newCts.Token;
        var capturedYear = CurrentYear;
        var capturedSeason = CurrentSeason;
        var cacheKey = (capturedYear, capturedSeason);
        bool foundInCache = _seasonalCache.TryGetValue(cacheKey, out var cached);
        bool hasCache = !force && foundInCache && cached != null;

        if (!hasCache) IsLoading = true;

        try
        {
            if (hasCache)
            {
                SetAllSeasonalItems(cached!);
                await HydrateMetadataAsync(_allSeasonalItems, ct);
                await HydrateCountryOriginAsync(_allSeasonalItems, ct);
                await ApplyFiltersAsync();

                // Only background-refresh current or upcoming seasons; past seasons are immutable history.
                if (IsCurrentOrFutureSeason(capturedYear, capturedSeason))
                {
                    _ = RefreshSeasonalCacheInBackground(capturedYear, capturedSeason, ct);
                }
            }
            else
            {
                SetAllSeasonalItems(new List<AnimeEntity>());
                var fresh = await _apiService.GetSeasonalAnimeAsync(capturedYear, capturedSeason, ct);
                if (ct.IsCancellationRequested) return;
                if (fresh != null && fresh.Any())
                {
                    await HydrateMetadataAsync(fresh, ct);
                    await HydrateCountryOriginAsync(fresh, ct);
                    SaveSeasonalCache(capturedYear, capturedSeason, fresh);
                    SetAllSeasonalItems(fresh);
                }
                await ApplyFiltersAsync();
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

    private bool IsCurrentOrFutureSeason(int year, string season)
    {
        int month = DateTime.UtcNow.Month;
        int clockYear = DateTime.UtcNow.Year;
        if (month == 12) clockYear++;

        string clockSeason = month switch
        {
            1 or 2 or 12 => Seasons[0],
            3 or 4 or 5 => Seasons[1],
            6 or 7 or 8 => Seasons[2],
            _ => Seasons[3]
        };

        if (year > clockYear) return true;
        if (year < clockYear) return false;

        int targetIdx = Seasons.IndexOf(season);
        int currentIdx = Seasons.IndexOf(clockSeason);
        return targetIdx >= currentIdx;
    }

    private async Task HydrateCountryOriginAsync(IReadOnlyList<AnimeEntity> items, CancellationToken ct)
    {
        if (items is null || items.Count == 0) return;

        try
        {
            bool updated = await _countryService.HydrateCountriesAsync(items, ct);
            if (updated)
            {
                var list = items as List<AnimeEntity> ?? items.ToList();
                SaveSeasonalCache(CurrentYear, CurrentSeason, list);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SeasonalViewModel: HydrateCountryOriginAsync failed");
        }
    }

    private async Task HydrateMetadataAsync(IReadOnlyList<AnimeEntity> items, CancellationToken ct)
    {
        if (items is null || items.Count == 0) return;
        var missingIds = items
            .Where(x => string.IsNullOrEmpty(x.RussianTitle) || string.IsNullOrEmpty(x.RussianSynopsis))
            .Select(x => x.Id)
            .ToList();

        if (missingIds.Count == 0) return;

        try
        {
            var metaDict = await _metadataRepo.GetBatchAsync(missingIds, ct);
            if (metaDict.Count == 0) return;

            bool updatedAny = false;
            foreach (var item in items)
            {
                if (metaDict.TryGetValue(item.Id, out var meta))
                {
                    if (string.IsNullOrEmpty(item.RussianTitle) && !string.IsNullOrEmpty(meta.Russian))
                    {
                        item.RussianTitle = meta.Russian;
                        updatedAny = true;
                    }
                    if (string.IsNullOrEmpty(item.RussianSynopsis) && !string.IsNullOrEmpty(meta.Description))
                    {
                        item.RussianSynopsis = Kiriha.Utils.Parsing.AnimeStringHelper.CleanShikiDescription(meta.Description);
                        updatedAny = true;
                    }
                }
            }

            if (updatedAny)
            {
                var list = items as List<AnimeEntity> ?? items.ToList();
                SaveSeasonalCache(CurrentYear, CurrentSeason, list);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SeasonalViewModel: HydrateMetadataAsync failed");
        }
    }

    private Task RefreshSeasonalCacheInBackground(int year, string season, CancellationToken ct) =>
        Task.Run(async () =>
        {
            try
            {
                var fresh = await _apiService.GetSeasonalAnimeAsync(year, season, ct);
                if (ct.IsCancellationRequested) return;
                if (fresh is null || !fresh.Any()) return;

                await HydrateMetadataAsync(fresh, ct);
                await HydrateCountryOriginAsync(fresh, ct);

                SaveSeasonalCache(year, season, fresh);
                if (year == CurrentYear && season == CurrentSeason)
                {
                    SetAllSeasonalItems(fresh);
                    await ApplyFiltersAsync();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Debug(ex, "Background seasonal refresh failed");
            }
        }, ct);

    private void SaveSeasonalCache(int year, string season, List<AnimeEntity> items)
    {
        _seasonalCache[(year, season)] = items;
        _ = _cacheStore.SaveAsync(year, season, items);
    }

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
