using Kiriha.Services.Data.Settings;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data;

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

        if (toUnhide == null) return;

        foreach (var id in toUnhide)
        {
            _hiddenSeasonalIds.Remove(id);
        }

        _settingsService.Update(settings =>
        {
            foreach (var id in toUnhide)
                settings.UI.HiddenSeasonalIds?.Remove(id);
        }, Kiriha.Core.Abstractions.Services.SettingsSection.UI, save: false);
        _ = _settingsService.SaveAsync();
    }

    [RelayCommand]
    public void ToggleHiddenSeasonal(AnimeEntity? item)
    {
        if (item == null) return;

        bool isHidden = _hiddenSeasonalIds.Contains(item.Id);
        if (!isHidden && item.Status != UserAnimeStatus.None) return;

        _settingsService.Update(settings =>
        {
            var list = settings.UI.HiddenSeasonalIds ??= new List<int>();
            if (isHidden)
            {
                _hiddenSeasonalIds.Remove(item.Id);
                list.Remove(item.Id);
                item.IsHiddenInSeasons = false;
            }
            else
            {
                _hiddenSeasonalIds.Add(item.Id);
                if (!list.Contains(item.Id)) list.Add(item.Id);
                item.IsHiddenInSeasons = true;
            }
        }, Kiriha.Core.Abstractions.Services.SettingsSection.UI, save: false);
        _ = _settingsService.SaveAsync();
        ApplyFilters();
    }
}
