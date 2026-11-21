using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Utils.Async;
using Kiriha.Infrastructure.Caching;

namespace Kiriha.ViewModels.Seasonal;

#pragma warning disable MVVMTK0034

public partial class SeasonalViewModel
{
    private void LoadSettingsState()
    {
        var settings = _settingsService.Current.UI;
        _sortBy = settings.SeasonalSortBy;
        _filterNsfw = settings.ShowNsfw;
        _showHidden = settings.SeasonalShowHidden;
        _hiddenSeasonalIds = new HashSet<int>(_seasonalHiddenRepo.GetHiddenIds());
        _filterNotInList = settings.SeasonalStatusFilters.Contains("NotInList");
        _filterWatching = settings.SeasonalStatusFilters.Contains("Watching");
        _filterCompleted = settings.SeasonalStatusFilters.Contains("Completed");
        _filterOnHold = settings.SeasonalStatusFilters.Contains("OnHold");
        _filterPlanToWatch = settings.SeasonalStatusFilters.Contains("PlanToWatch");
        _filterDropped = settings.SeasonalStatusFilters.Contains("Dropped");
        _filterFranchise = settings.SeasonalStatusFilters.Contains("Franchise");
    }

    private void SetCurrentSeasonFromClock() =>
        (_currentYear, _currentSeason) = SeasonalCacheStore.GetCurrentSeason(_clock.GetUtcNow().UtcDateTime);

    private Debouncer CreateSettingsDebouncer() =>
        new(TimeSpan.FromMilliseconds(500), async (ct) =>
        {
            _settingsService.Update(settings =>
            {
                var s = settings.UI;
                s.SeasonalSortBy = SortBy;
                s.ShowNsfw = FilterNsfw;
                s.SeasonalShowHidden = ShowHidden;

                s.SeasonalStatusFilters.Clear();
                if (FilterNotInList) s.SeasonalStatusFilters.Add("NotInList");
                if (FilterWatching) s.SeasonalStatusFilters.Add("Watching");
                if (FilterCompleted) s.SeasonalStatusFilters.Add("Completed");
                if (FilterOnHold) s.SeasonalStatusFilters.Add("OnHold");
                if (FilterPlanToWatch) s.SeasonalStatusFilters.Add("PlanToWatch");
                if (FilterDropped) s.SeasonalStatusFilters.Add("Dropped");
                if (FilterFranchise) s.SeasonalStatusFilters.Add("Franchise");
            }, SettingsSection.UI, save: false);

            await _settingsService.SaveAsync();
        });

    private void SaveSettingsDebounced()
    {
        if (_isInitializing) return;
        _filterDebouncer.Invoke();
    }
}

#pragma warning restore MVVMTK0034
