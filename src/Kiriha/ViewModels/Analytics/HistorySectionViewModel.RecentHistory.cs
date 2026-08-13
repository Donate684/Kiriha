using Kiriha.Models.Entities;
using Kiriha.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kiriha.ViewModels.Analytics;

public partial class HistorySectionViewModel
{
    private void AddRecentHistory(IEnumerable<HistoryItem> history, IReadOnlyCollection<AnimeEntity> items)
    {
        var today = DateTime.UtcNow.Date;
        var posterMap = items
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First().MainPictureUrl);
        var watched = history
            .Where(x => x.ActionType is 1 or 4 or 6)
            .Where(x => x.Timestamp.Date < today && x.Timestamp.Date >= today.AddDays(-RecentHistoryDays))
            .ToList();

        var grouped = watched
            .GroupBy(x => (today - x.Timestamp.Date).Days)
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
                Tooltip = $"{date:dd.MM}: {count} эп."
            };

            foreach (var entry in entries?.OrderByDescending(x => x.Timestamp) ?? Enumerable.Empty<HistoryItem>())
            {
                posterMap.TryGetValue(entry.AnimeId, out var posterUrl);
                point.Entries.Add(new AnalyticsHistoryEntry
                {
                    Title = entry.RussianTitle ?? entry.AnimeTitle,
                    Subtitle = entry.RussianTitle != null ? entry.AnimeTitle : null,
                    Detail = entry.Episode > 0
                        ? $"Серия {entry.Episode} · {entry.Timestamp:HH:mm}"
                        : entry.Timestamp.ToString("HH:mm", CultureInfo.CurrentCulture),
                    PosterUrl = posterUrl
                });
            }

            RecentHistory.Add(point);
        }
    }
}
