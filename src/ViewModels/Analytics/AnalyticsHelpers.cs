using Kiriha.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Kiriha.Core;
using Kiriha.Core.Constants;
using Kiriha.Models.Entities;

namespace Kiriha.ViewModels.Analytics;

public static class AnalyticsHelpers
{
    public static double EstimateHoursWatched(IEnumerable<AnimeItem> items)
    {
        return items.Sum(item =>
        {
            var episodeMinutes = string.Equals(item.Type, AppConstants.AnimeTypes.Movie, StringComparison.OrdinalIgnoreCase)
                ? 95
                : 24;
            return Math.Max(0, item.Progress) * episodeMinutes / 60.0;
        });
    }

    public static double Percent(int value, int max) => Math.Clamp(value * 100.0 / Math.Max(1, max), 0, 100);

    public static string GetAccent(string label)
    {
        var hash = Math.Abs(label.GetHashCode());
        var palette = new[]
        {
            "#FF0F7B83",
            "#FF2D7DD2",
            "#FFD17A22",
            "#FF7B61FF",
            "#FF2E9D62",
            "#FFD1495B",
            "#FF5C80BC",
            "#FF8E6C88"
        };
        return palette[hash % palette.Length];
    }

    public static string GetStatusLabel(UserAnimeStatus status)
    {
        return status switch
        {
            UserAnimeStatus.Watching => UIUtils.GetLoc("anime.status.watching"),
            UserAnimeStatus.Completed => UIUtils.GetLoc("anime.status.completed"),
            UserAnimeStatus.OnHold => UIUtils.GetLoc("anime.status.on_hold"),
            UserAnimeStatus.Dropped => UIUtils.GetLoc("anime.status.dropped"),
            UserAnimeStatus.PlanToWatch => UIUtils.GetLoc("anime.status.plan_to_watch"),
            _ => UIUtils.GetLoc("anime.status.unknown")
        };
    }

    public static string GetStatusAccent(UserAnimeStatus status)
    {
        return status switch
        {
            UserAnimeStatus.Watching => "#FF2D7DD2",
            UserAnimeStatus.Completed => "#FF2E9D62",
            UserAnimeStatus.OnHold => "#FFD17A22",
            UserAnimeStatus.Dropped => "#FFE53935",
            UserAnimeStatus.PlanToWatch => "#FF7B61FF",
            _ => "#FF6B7280"
        };
    }

    public static string GetScoreAccent(int score) => score switch
    {
        10 or 9 => "#FF2E9D62",
        8 or 7 => "#FF2D7DD2",
        6 or 5 => "#FFD17A22",
        4 or 3 => "#FFD1495B",
        _ => "#FFE53935"
    };
}

