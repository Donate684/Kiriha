using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Models;
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
    public async Task LoadSeasonalAnimeAsync()
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
        bool hasCache = _seasonalCache.TryGetValue(cacheKey, out var cached);

        if (!hasCache) IsLoading = true;

        try
        {
            if (hasCache)
            {
                SetAllSeasonalItems(cached!);
                await HydrateMetadataAsync(_allSeasonalItems, ct);
                await ApplyFiltersAsync();
                _ = RefreshSeasonalCacheInBackground(capturedYear, capturedSeason, ct);
            }
            else
            {
                SetAllSeasonalItems(new List<AnimeEntity>());
                var fresh = await _apiService.GetSeasonalAnimeAsync(capturedYear, capturedSeason, ct);
                if (ct.IsCancellationRequested) return;
                if (fresh != null && fresh.Any())
                {
                    await HydrateMetadataAsync(fresh, ct);
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
