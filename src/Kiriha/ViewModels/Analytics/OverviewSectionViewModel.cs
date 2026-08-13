using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Localization;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.ViewModels.Analytics;

public partial class OverviewSectionViewModel : ViewModelBase
{
    public ObservableCollection<AnalyticsMetric> Metrics { get; } = new();
    public ObservableCollection<AnalyticsBar> StatusDistribution { get; } = new();
    public ObservableCollection<AnalyticsBar> ScoreDistribution { get; } = new();

    public void Refresh(IReadOnlyCollection<AnimeEntity> items, IReadOnlyCollection<AnimeEntity> nonPlanned, IReadOnlyCollection<AnimeEntity> completed, IReadOnlyCollection<int> scored)
    {
        Metrics.Clear();
        StatusDistribution.Clear();
        ScoreDistribution.Clear();

        if (items.Count == 0) return;

        var totalEpisodes = items.Sum(x => Math.Max(0, x.Progress));
        var approximateHours = AnalyticsHelpers.EstimateHoursWatched(items);
        var meanScore = scored.Count > 0 ? scored.Average() : 0;
        var completionRate = items.Count > 0 ? completed.Count * 100.0 / items.Count : 0;

        Metrics.Add(new AnalyticsMetric { Label = LocalizationStore.Translate("analytics.overview.total_titles"), Value = items.Count.ToString("N0"), Hint = LocalizationStore.Translate("analytics.overview.in_local_library") });
        Metrics.Add(new AnalyticsMetric { Label = LocalizationStore.Translate("analytics.overview.completed"), Value = completed.Count.ToString("N0"), Hint = $"{completionRate:0.#}{LocalizationStore.Translate("analytics.overview.percent_of_list")}" });
        Metrics.Add(new AnalyticsMetric { Label = LocalizationStore.Translate("analytics.overview.mean_score"), Value = scored.Count > 0 ? meanScore.ToString("0.00") : "-", Hint = $"{scored.Count:N0} {LocalizationStore.Translate("analytics.overview.scores_count")}" });
        Metrics.Add(new AnalyticsMetric { Label = LocalizationStore.Translate("analytics.overview.episodes"), Value = totalEpisodes.ToString("N0"), Hint = string.Format(LocalizationStore.Translate("analytics.overview.approx_hours"), approximateHours.ToString("N0")) });

        AddStatusDistribution(items);
        AddScoreDistribution(scored);
    }

    private void AddStatusDistribution(IReadOnlyCollection<AnimeEntity> items)
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
                ShareText = $"{percent:0.#}{LocalizationStore.Translate("analytics.overview.percent_of_list")}",
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

