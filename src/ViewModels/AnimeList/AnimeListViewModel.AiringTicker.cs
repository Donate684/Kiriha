using System;
using Avalonia.Threading;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel
{
    // Per-minute ticker that re-evaluates the airing countdown ("Hч Mм") on
    // every visible card. Without it the pill text would only refresh when
    // NextEpisodeAt itself changed (i.e. on the next 12 h sync), so a card
    // could sit stuck on "3ч 19м" for hours.
    private DispatcherTimer? _airingTicker;

    private void OnAiringTick(object? sender, EventArgs e)
    {
        // Only items with a future-dated next episode (or recently-aired
        // unconfirmed state, i.e. up to 48 h overdue - see AiringBadgeText)
        // need their countdown re-evaluated. Skipping the rest keeps the
        // tick essentially free even on large libraries.
        var now = DateTime.Now;
        foreach (var item in FilteredItems)
        {
            if (item.NextEpisodeAt.HasValue)
            {
                var diff = item.NextEpisodeAt.Value - now;
                if (diff.TotalHours < -48) continue;
                item.RefreshAiringBadge();
            }
            else if (item.Presentation.IsNewEpisode)
            {
                // The 2-day "new episode" window is also time-dependent.
                item.RefreshAiringBadge();
            }
        }
    }
}
