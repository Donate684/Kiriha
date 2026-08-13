using Kiriha.ViewModels.Main;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Dialogs;
using Kiriha.ViewModels.Startup;
using Kiriha.ViewModels.Settings;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Image;
using Kiriha.Services.Data.Mapping;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Kiriha.Services.Data;
using Kiriha.ViewModels.Seasonal;
using Serilog;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsCacheViewModel : ObservableObject
{
    private readonly CacheCleanupService _cacheCleanupService;
    private readonly ImageCacheService _imageCacheService;
    private readonly MappingService _mappingService;
    private readonly SeasonalViewModel _seasonalViewModel;

    public SettingsCacheViewModel(
        CacheCleanupService cacheCleanupService,
        ImageCacheService imageCacheService,
        MappingService mappingService,
        SeasonalViewModel seasonalViewModel)
    {
        _cacheCleanupService = cacheCleanupService;
        _imageCacheService = imageCacheService;
        _mappingService = mappingService;
        _seasonalViewModel = seasonalViewModel;

        InitializeCacheItems();
        _ = RefreshCacheStats();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClearSelectedCache))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectedCacheCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCacheStatsCommand))]
    private bool _isCacheBusy;

    [ObservableProperty]
    private string _cacheStatus = string.Empty;

    public ObservableCollection<CacheCleanupItem> CacheItems { get; } = new()
    {
        new CacheCleanupItem(CacheCleanupTarget.History, "settings.cache.items.history"),
        new CacheCleanupItem(CacheCleanupTarget.ImageFiles, "settings.cache.items.images"),
        new CacheCleanupItem(CacheCleanupTarget.ApiCache, "settings.cache.items.api"),
        new CacheCleanupItem(CacheCleanupTarget.RecognitionCache, "settings.cache.items.recognition"),
        new CacheCleanupItem(CacheCleanupTarget.SeasonalCache, "settings.cache.items.seasonal")
    };

    public bool CanClearSelectedCache => !IsCacheBusy && CacheItems.Any(x => x.IsSelected);

    private void InitializeCacheItems()
    {
        foreach (var item in CacheItems)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CacheCleanupItem.IsSelected))
                {
                    OnPropertyChanged(nameof(CanClearSelectedCache));
                    ClearSelectedCacheCommand.NotifyCanExecuteChanged();
                }
            };
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshCacheStats))]
    private async Task RefreshCacheStats()
    {
        IsCacheBusy = true;
        try
        {
            var stats = await _cacheCleanupService.GetStatsAsync();
            foreach (var stat in stats)
            {
                var item = CacheItems.FirstOrDefault(x => x.Target == stat.Target);
                if (item == null) continue;
                item.ItemCount = stat.ItemCount;
                item.SizeBytes = stat.SizeBytes;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh cache stats");
            CacheStatus = UIUtils.GetLoc("common.errors.generic");
        }
        finally
        {
            IsCacheBusy = false;
        }
    }

    private bool CanRefreshCacheStats() => !IsCacheBusy;

    [RelayCommand(CanExecute = nameof(CanClearSelectedCache))]
    private async Task ClearSelectedCache()
    {
        var selected = CacheItems.Where(x => x.IsSelected).Select(x => x.Target).ToList();
        if (selected.Count == 0) return;

        IsCacheBusy = true;
        CacheStatus = string.Empty;
        try
        {
            await _cacheCleanupService.ClearAsync(selected);
            InvalidateRuntimeCaches(selected);
            foreach (var item in CacheItems) item.IsSelected = false;
            CacheStatus = UIUtils.GetLoc("settings.cache.cleared");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clear selected cache");
            CacheStatus = UIUtils.GetLoc("common.errors.generic");
        }
        finally
        {
            IsCacheBusy = false;
            await RefreshCacheStats();
        }
    }

    private void InvalidateRuntimeCaches(IReadOnlyCollection<CacheCleanupTarget> selected)
    {
        if (selected.Contains(CacheCleanupTarget.ImageFiles))
            _imageCacheService.ClearMemoryCache();

        if (selected.Contains(CacheCleanupTarget.RecognitionCache))
            _mappingService.ClearRecognitionCaches();

        if (selected.Contains(CacheCleanupTarget.SeasonalCache))
            _seasonalViewModel.InvalidateCache();
    }
}
