using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.ViewModels.Analytics;

public partial class AnalyticsViewModel
{
    [RelayCommand]
    private void OpenFavorite(AnalyticsFavoriteRow? row)
    {
        if (row == null || row.Entries.Count == 0)
        {
            return;
        }

        HistoryPopupTitle = row.Name;
        HistoryPopupSubtitle = $"{row.Count} тайтл. • средняя {row.MeanScore} • вес {row.WeightedScore}";
        ShowHistoryPopup(row.Entries);
    }

    [RelayCommand]
    private void OpenDailyHistory(AnalyticsDailyHistoryPoint? point)
    {
        if (point == null || point.Count == 0) return;

        HistoryPopupTitle = point.DaysAgo == 1
            ? "Вчера"
            : $"{point.DaysAgo} дн. назад";
        HistoryPopupSubtitle = $"{point.DateLabel} · {point.Count} эп. · {point.Entries.Select(x => x.Title).Distinct().Count()} тайтл(ов)";
        ShowHistoryPopup(point.Entries);
    }

    [RelayCommand]
    private void OpenMonthlyHistory(AnalyticsMonthlyHistoryCell? cell)
    {
        if (cell == null || cell.Count == 0) return;

        HistoryPopupTitle = $"{cell.MonthName} · завершено";
        HistoryPopupSubtitle = $"{cell.Count} тайтл(ов)";
        ShowHistoryPopup(cell.Entries);
    }

    [RelayCommand]
    private void CloseHistoryPopup()
    {
        IsHistoryPopupOpen = false;
        HistoryPopupEntries.Clear();
    }

    private void ShowHistoryPopup(IEnumerable<AnalyticsHistoryEntry> entries)
    {
        HistoryPopupEntries.Clear();
        foreach (var entry in entries)
        {
            HistoryPopupEntries.Add(entry);
        }

        IsHistoryPopupOpen = true;
    }

    private static double Percent(int value, int max) => Math.Clamp(value * 100.0 / Math.Max(1, max), 0, 100);

    private static double PercentDouble(double value, double max) => Math.Clamp(value * 100.0 / Math.Max(0.01, max), 0, 100);

    private static string GetAccent(string label)
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

    private static string GetStatusLabel(UserAnimeStatus status)
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

    private static string GetStatusAccent(UserAnimeStatus status)
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
}
