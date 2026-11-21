using Avalonia.Threading;
using Kiriha.Core.Domain.Utils.Async;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel
{
    // Per-minute ticker that re-evaluates the airing countdown ("H? M?") on
    // every visible card. Without it the pill text would only refresh when
    // NextEpisodeAt itself changed (i.e. on the next 12 h sync), so a card
    // could sit stuck on "3? 19?" for hours.
    private DispatcherTimer? _airingTicker;
    private readonly Dictionary<int, DateTime> _lastAiringTickAttempts = [];

    private void OnAiringTick(object? sender, EventArgs e)
    {
        if (_isDisposed) return;

        // Only items with a future-dated next episode (or recently-aired
        // unconfirmed state, i.e. up to 48 h overdue - see AiringBadgeText)
        // need their countdown re-evaluated. Skipping the rest keeps the
        // tick essentially free even on large libraries.
        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-15);
        foreach (var attempt in _lastAiringTickAttempts.Where(entry => entry.Value <= cutoff).ToArray())
            _lastAiringTickAttempts.Remove(attempt.Key);

        foreach (var item in FilteredItems)
        {
            if (item.NextEpisodeAt.HasValue)
            {
                var diff = item.NextEpisodeAt.Value - now;
                if (diff.TotalHours < -48) continue;
                
                if (diff.TotalSeconds <= 0)
                {
                    // Episode has theoretically aired. Trigger an immediate sync from AniList
                    // if we haven't done so recently, so it doesn't stay stuck on "New ep.?"
                    // until the 6-hour background sync task runs.
                    if ((item.LastEpisodesSync is null || item.LastEpisodesSync < cutoff) &&
                        _lastAiringTickAttempts.TryAdd(item.Id, now))
                    {
                        _airingInfoService.SyncEpisodesForAnimeAsync(item).SafeFireAndForget();
                    }
                }

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
