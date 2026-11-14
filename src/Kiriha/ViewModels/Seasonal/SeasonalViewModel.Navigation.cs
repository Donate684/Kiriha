using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Utils.Async;

namespace Kiriha.ViewModels.Seasonal;

public partial class SeasonalViewModel
{
    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DisplaySeason));
        OnPropertyChanged(nameof(DisplaySortBy));
        foreach (var item in _allSeasonalItems) item.RefreshMetadata();
    }

    public void EnqueueItemForViewport(AnimeEntity item)
    {
        if (item is null) return;
        _queueService.EnqueueForViewport([item]);
        _franchiseService.EnqueueForResolution(item);
    }

    public void EnqueueItemsForViewport(IEnumerable<AnimeEntity> items)
    {
        _queueService.EnqueueForViewport(items);
        foreach (var item in items)
        {
            if (item != null)
            {
                _franchiseService.EnqueueForResolution(item);
            }
        }
    }

    private bool _suppressSeasonReload;

    [RelayCommand]
    public void NextSeason()
    {
        int idx = Seasons.IndexOf(CurrentSeason);
        if (idx == 3)
        {
            SetSeasonAndYear(Seasons[0], CurrentYear + 1);
        }
        else
        {
            CurrentSeason = Seasons[idx + 1];
        }
    }

    [RelayCommand]
    public void PreviousSeason()
    {
        int idx = Seasons.IndexOf(CurrentSeason);
        if (idx == 0)
        {
            SetSeasonAndYear(Seasons[3], CurrentYear - 1);
        }
        else
        {
            CurrentSeason = Seasons[idx - 1];
        }
    }

    private void SetSeasonAndYear(string season, int year)
    {
        _suppressSeasonReload = true;
        try
        {
            CurrentSeason = season;
            CurrentYear = year;
        }
        finally
        {
            _suppressSeasonReload = false;
        }

        if (!_isInitializing)
        {
            LoadSeasonalAnimeAsync().SafeFireAndForget("LoadSeasonalAnimeAsync");
        }
    }

    partial void OnCurrentYearChanged(int value)
    {
        if (_isInitializing || _suppressSeasonReload) return;
        LoadSeasonalAnimeAsync().SafeFireAndForget("LoadSeasonalAnimeAsync");
    }

    partial void OnCurrentSeasonChanged(string value)
    {
        if (_isInitializing || _suppressSeasonReload) return;
        LoadSeasonalAnimeAsync().SafeFireAndForget("LoadSeasonalAnimeAsync");
    }
}
