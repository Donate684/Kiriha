using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kiriha.Localization;

namespace Kiriha.ViewModels.Analytics;

public partial class HistorySectionViewModel
{
    private void AddRecentHistory(IEnumerable<HistoryItem> history, IReadOnlyCollection<AnimeEntity> items)
    {
        var today = DateTime.Today;
        var posterMap = items
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First().MainPictureUrl);
        var watched = history
            .Where(x => x.ActionType is 1 or 4 or 6)
            .Where(x => x.Timestamp.ToLocalTime().Date < today && x.Timestamp.ToLocalTime().Date >= today.AddDays(-RecentHistoryDays))
            .ToList();

        var grouped = watched
            .GroupBy(x => (today - x.Timestamp.ToLocalTime().Date).Days)
            .ToDictionary(x => x.Key, x => x.ToList());
        var max = Math.Max(1, grouped.Values.Select(x => x.Count).DefaultIfEmpty().Max());

        RecentHistoryEpisodes = watched.Count;
        RecentHistoryTitles = watched.Select(x => x.AnimeId).Distinct().Count();

        for (var daysAgo = RecentHistoryDays; daysAgo >= 1; daysAgo--)
        {
            grouped.TryGetValue(daysAgo, out var entries);
            var count = entries?.Count ?? 0;
            var date = today.AddDays(-daysAgo);
            var percent = count / (double)max;
            var point = new AnalyticsDailyHistoryPoint
            {
                DaysAgo = daysAgo,
                Label = daysAgo.ToString(CultureInfo.InvariantCulture),
                DateLabel = date.ToString("dd.MM", CultureInfo.CurrentCulture),
                Count = count,
                BarHeight = 3 + percent * 104,
                Alpha = count == 0 ? 0.16 : 0.35 + percent * 0.65,
                CountLabel = count > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty,
                ShowCountInBar = percent >= 0.32,
                Tooltip = string.Format(LocalizationStore.Translate("analytics.history.episodes_format"), $"{date:dd.MM}: {count}")
            };

            if (entries != null)
            {
                foreach (var entry in entries.OrderByDescending(x => x.Timestamp))
                {
                    posterMap.TryGetValue(entry.AnimeId, out var posterUrl);
                    point.Entries.Add(new AnalyticsHistoryEntry
                    {
                        Title = entry.RussianTitle ?? entry.AnimeTitle,
                        Subtitle = entry.RussianTitle != null ? entry.AnimeTitle : null,
                        Detail = entry.Episode > 0
                            ? string.Format(LocalizationStore.Translate("analytics.history.episode_format_2"), entry.Episode, entry.Timestamp.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture))
                            : entry.Timestamp.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture),
                        PosterUrl = posterUrl
                    });
                }
            }

            RecentHistory.Add(point);
        }
    }
}
