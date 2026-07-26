using Kiriha.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Models.Entities;

namespace Kiriha.ViewModels.Analytics;

public partial class OverviewSectionViewModel : ViewModelBase
{
    public ObservableCollection<AnalyticsMetric> Metrics { get; } = new();
    public ObservableCollection<AnalyticsBar> StatusDistribution { get; } = new();
    public ObservableCollection<AnalyticsBar> ScoreDistribution { get; } = new();

    public void Refresh(IReadOnlyCollection<AnimeItem> items, IReadOnlyCollection<AnimeItem> nonPlanned, IReadOnlyCollection<AnimeItem> completed, IReadOnlyCollection<int> scored)
    {
        Metrics.Clear();
        StatusDistribution.Clear();
        ScoreDistribution.Clear();

        if (items.Count == 0) return;

        var totalEpisodes = items.Sum(x => Math.Max(0, x.Progress));
        var approximateHours = AnalyticsHelpers.EstimateHoursWatched(items);
        var meanScore = scored.Count > 0 ? scored.Average() : 0;
        var completionRate = items.Count > 0 ? completed.Count * 100.0 / items.Count : 0;

        Metrics.Add(new AnalyticsMetric { Label = "Всего тайтлов", Value = items.Count.ToString("N0"), Hint = "в локальной библиотеке" });
        Metrics.Add(new AnalyticsMetric { Label = "Завершено", Value = completed.Count.ToString("N0"), Hint = $"{completionRate:0.#}% списка" });
        Metrics.Add(new AnalyticsMetric { Label = "Средняя оценка", Value = scored.Count > 0 ? meanScore.ToString("0.00") : "-", Hint = $"{scored.Count:N0} оценок" });
        Metrics.Add(new AnalyticsMetric { Label = "Эпизодов", Value = totalEpisodes.ToString("N0"), Hint = $"примерно {approximateHours:N0} ч" });

        AddStatusDistribution(items);
        AddScoreDistribution(scored);
    }

    private void AddStatusDistribution(IReadOnlyCollection<AnimeItem> items)
    {
        var groups = items
            .GroupBy(x => x.Status)
            .Select(x => new
            {
                Status = x.Key,
                Count = x.Count()
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        foreach (var group in groups)
        {
            var percent = items.Count > 0 ? group.Count * 100.0 / items.Count : 0;
            StatusDistribution.Add(new AnalyticsBar
            {
                Label = AnalyticsHelpers.GetStatusLabel(group.Status),
                Value = group.Count.ToString("N0"),
                Count = group.Count,
                Percent = AnalyticsHelpers.Percent(group.Count, items.Count),
                ShareText = $"{percent:0.#}% списка",
                Accent = AnalyticsHelpers.GetStatusAccent(group.Status)
            });
        }
    }

    private void AddScoreDistribution(IReadOnlyCollection<int> scores)
    {
        Span<int> counts = stackalloc int[11];
        foreach (var score in scores)
        {
            if (score is >= 1 and <= 10)
            {
                counts[score]++;
            }
        }

        var maxCount = 0;
        for (var score = 1; score <= 10; score++)
        {
            maxCount = Math.Max(maxCount, counts[score]);
        }

        for (var score = 10; score >= 1; score--)
        {
            var count = counts[score];
            ScoreDistribution.Add(new AnalyticsBar
            {
                Label = score.ToString(),
                Value = count.ToString("N0"),
                Count = count,
                Percent = AnalyticsHelpers.Percent(count, maxCount),
                Accent = AnalyticsHelpers.GetScoreAccent(score),
                BarHeight = count == 0 ? 0 : 6 + (count / (double)Math.Max(1, maxCount)) * 66
            });
        }
    }
}

