using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Settings;

namespace Kiriha.ViewModels.Seasonal;

public partial class SeasonalViewModel
{
    public void UpdateUserList(Dictionary<int, UserAnimeStatus> userList)
    {
        _userAnimeStore = userList;
        UnhideTrackedTitles(userList);
        ApplyFilters();
    }

    private void UnhideTrackedTitles(Dictionary<int, UserAnimeStatus> userList)
    {
        if (_hiddenSeasonalIds.Count == 0) return;

        List<int>? toUnhide = null;
        foreach (var id in _hiddenSeasonalIds)
        {
            if (userList.TryGetValue(id, out var status) && status != UserAnimeStatus.None)
                (toUnhide ??= new List<int>()).Add(id);
        }

        if (toUnhide is null) return;

        foreach (var id in toUnhide)
        {
            _hiddenSeasonalIds.Remove(id);
        }

        _ = _seasonalHiddenRepo.RemoveRangeAsync(toUnhide);
    }

    [RelayCommand]
    public void ToggleHiddenSeasonal(AnimeEntity? item)
    {
        if (item is null) return;

        bool isHidden = _hiddenSeasonalIds.Contains(item.Id);
        if (!isHidden && item.Status != UserAnimeStatus.None) return;

        if (isHidden)
        {
            _hiddenSeasonalIds.Remove(item.Id);
            item.IsHiddenInSeasons = false;
            _ = _seasonalHiddenRepo.RemoveAsync(item.Id);
        }
        else
        {
            _hiddenSeasonalIds.Add(item.Id);
            item.IsHiddenInSeasons = true;
            _ = _seasonalHiddenRepo.AddAsync(item.Id);
        }

        if (isHidden)
        {
            // Unhiding: need full refilter to reinsert the item in correct sorted position
            ApplyFilters();
        }
        else
        {
            // Hiding: remove in-place to preserve scroll position
            DisplayItems.Remove(item);
            RefreshCategoryHeaders();
        }
    }
}
