using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Domain.Models.Entities;

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
        var watching = _animeRepo.Collection
            .Where(x => x.Status == UserAnimeStatus.Watching && x.MediaKind == MediaKind.Anime)
            .OrderByDescending(x => x.Presentation.ShowAiredProgressBar && x.Presentation.UnseenEpisodesCount > 0)
            .ThenByDescending(x => x.Presentation.UnseenEpisodesCount)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        WatchingAnime.Reset(watching.Where(a => !hidden.Contains(a.Id)));

        foreach (var oldItem in HideMenuItems)
        {
            oldItem.HiddenChanged -= OnHideMenuItemChanged;
        }

        HideMenuItems.Clear();
        foreach (var a in watching)
        {
            var item = new HideableAnimeItem(a, hidden.Contains(a.Id))
            {
                IsSelected = SelectedAnime != null && a.Id == SelectedAnime.Id
            };
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
            WatchingAnime.Reset(HideMenuItems.Where(h => !h.IsHidden).Select(h => h.Anime));
        }
    }
}
