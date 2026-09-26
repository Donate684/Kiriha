using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Settings;

namespace Kiriha.ViewModels.Torrents;

public partial class TorrentsViewModel
{
    [RelayCommand]
    public void ToggleHideMode() => IsHideMode = !IsHideMode;

    [RelayCommand]
    public void ToggleHideAnime(HideableAnimeItem? item)
    {
        if (item is null) return;
        item.IsHidden = !item.IsHidden;
    }

    public void RefreshWatchingList()
    {
        var hidden = new HashSet<int>(_torrentFilterRepo.GetHiddenAnimeIds());
        var watching = _animeRepo.Collection.Where(x => x.Status == UserAnimeStatus.Watching && x.MediaKind == MediaKind.Anime).ToList();

        WatchingAnime.Clear();
        foreach (var a in watching)
            if (!hidden.Contains(a.Id)) WatchingAnime.Add(a);

        HideMenuItems.Clear();
        foreach (var a in watching)
        {
            var item = new HideableAnimeItem(a, hidden.Contains(a.Id));
            item.HiddenChanged += OnHideMenuItemChanged;
            HideMenuItems.Add(item);
        }
    }

    private void OnHideMenuItemChanged(HideableAnimeItem item)
    {
        _ = _torrentFilterRepo.SetAnimeHiddenAsync(item.Anime.Id, item.IsHidden);

        if (item.IsHidden)
        {
            var existing = WatchingAnime.FirstOrDefault(a => a.Id == item.Anime.Id);
            if (existing != null) WatchingAnime.Remove(existing);
        }
        else if (!WatchingAnime.Any(a => a.Id == item.Anime.Id))
        {
            WatchingAnime.Add(item.Anime);
        }
    }
}
