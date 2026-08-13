using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kiriha.ViewModels.Analytics;

public partial class HistorySectionViewModel
{
    private void AddMonthlyHistory(IEnumerable<AnimeEntity> completed)
    {
        var monthGroups = completed
            .Where(x => x.DateCompleted.HasValue && x.DateCompleted.Value.Year > 1900)
            .GroupBy(x => new { x.DateCompleted!.Value.Year, x.DateCompleted.Value.Month })
            .ToDictionary(x => (x.Key.Year, x.Key.Month), x => x.ToList());

        HasMonthlyHistory = monthGroups.Count > 0;
        if (!HasMonthlyHistory) return;

        var max = Math.Max(1, monthGroups.Values.Max(x => x.Count));
        var now = DateTime.UtcNow;
        var minYear = Math.Min(monthGroups.Keys.Min(x => x.Year), now.Year);
        var maxYear = Math.Max(monthGroups.Keys.Max(x => x.Year), now.Year);
        var monthNames = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedMonthNames;

        for (var year = maxYear; year >= minYear; year--)
        {
            var row = new AnalyticsMonthlyHistoryRow { Year = year };
            for (var month = 1; month <= 12; month++)
            {
                monthGroups.TryGetValue((year, month), out var entries);
                var count = entries?.Count ?? 0;
                var mean = entries?
                    .Select(x => int.TryParse(x.Score, out var score) ? score : 0)
                    .Where(x => x > 0)
                    .DefaultIfEmpty()
                    .Average() ?? 0;
                var intensity = count == 0 ? 0 : count / (double)max;
                var alpha = count == 0
                    ? (byte)0x10
                    : (byte)Math.Round(0x32 + intensity * (0xFF - 0x32));

                var cell = new AnalyticsMonthlyHistoryCell
                {
                    Month = month,
                    MonthName = monthNames[month - 1],
                    Count = count,
                    Alpha = count == 0 ? 0.06 : 0.22 + count / (double)max * 0.78,
                    Fill = $"#{alpha:X2}2D7DD2",
                    TextColor = intensity >= 0.48 ? "#FFFFFFFF" : "#FF1F2937",
                    IsCurrentMonth = year == now.Year && month == now.Month,
                    Tooltip = mean > 0
                        ? $"{monthNames[month - 1]} {year}: {count} завершено, средняя {mean:0.00}"
                        : $"{monthNames[month - 1]} {year}: {count} завершено"
                };

                foreach (var entry in entries?.OrderBy(x => x.Presentation.DisplayTitle) ?? Enumerable.Empty<AnimeEntity>())
                {
                    cell.Entries.Add(new AnalyticsHistoryEntry
                    {
                        Title = entry.Presentation.DisplayTitle,
                        Subtitle = entry.RussianTitle != null ? entry.Title : null,
                        Detail = int.TryParse(entry.Score, out var score) && score > 0
                            ? $"Оценка {score}"
                            : "Без оценки",
                        PosterUrl = entry.MainPictureUrl
                    });
                }

                row.Months.Add(cell);
            }

            MonthlyHistory.Add(row);
        }
    }
}
