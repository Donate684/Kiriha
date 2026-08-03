using Kiriha.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kiriha.Core.Localization;

namespace Kiriha.ViewModels.Analytics;

public partial class HistorySectionViewModel
{
    private void AddYearDistribution(IEnumerable<AnimeItem> completed)
    {
        var groups = completed
            .Where(x => x.StartYear.HasValue)
            .GroupBy(x => x.StartYear!.Value)
            .Select(x => new { Year = x.Key, Count = x.Count() })
            .OrderByDescending(x => x.Year)
            .Take(12)
            .OrderBy(x => x.Year)
            .ToList();

        var max = groups.Count == 0 ? 1 : groups.Max(x => x.Count);
        foreach (var group in groups)
        {
            YearDistribution.Add(new AnalyticsBar
            {
                Label = group.Year.ToString(),
                Value = group.Count.ToString("N0"),
                Count = group.Count,
                Percent = AnalyticsHelpers.Percent(group.Count, max)
            });
        }
    }

    private void AddReleaseYearCompletions(IEnumerable<AnimeItem> completed)
    {
        var groups = completed
            .Where(x => x.StartYear.HasValue)
            .GroupBy(x => x.StartYear!.Value)
            .Select(x => new { Year = x.Key, Count = x.Count() })
            .OrderByDescending(x => x.Year)
            .ToList();

        var max = groups.Count == 0 ? 1 : groups.Max(x => x.Count);
        foreach (var group in groups)
        {
            var intensity = group.Count / (double)max;
            var alpha = (byte)Math.Round(0x24 + intensity * (0xFF - 0x24));
            ReleaseYearCompletions.Add(new AnalyticsBar
            {
                Label = group.Year.ToString(CultureInfo.InvariantCulture),
                Value = group.Count.ToString("N0"),
                Count = group.Count,
                Percent = AnalyticsHelpers.Percent(group.Count, max),
                Alpha = 0.16 + intensity * 0.84,
                ShareText = string.Format(LocalizationStore.Translate("analytics.history.titles_format"), group.Count.ToString("N0")),
                Accent = $"#{alpha:X2}2D7DD2",
                TextColor = intensity >= 0.48 ? "#FFFFFFFF" : "#FF1F2937"
            });
        }
    }
}
