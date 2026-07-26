using Kiriha.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models.Entities;

namespace Kiriha.ViewModels.Analytics;

public partial class HistorySectionViewModel : ViewModelBase
{
    private const int RecentHistoryDays = 14;

    public ObservableCollection<AnalyticsDailyHistoryPoint> RecentHistory { get; } = new();
    public ObservableCollection<AnalyticsMonthlyHistoryRow> MonthlyHistory { get; } = new();
    public ObservableCollection<AnalyticsBar> YearDistribution { get; } = new();
    public ObservableCollection<AnalyticsBar> ReleaseYearCompletions { get; } = new();
    
    public IReadOnlyList<string> MonthHeaders { get; } = new[]
    {
        "Jan", "Feb", "Mar", "Apr", "May", "Jun",
        "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
    };

    [ObservableProperty] private int _recentHistoryEpisodes;
    [ObservableProperty] private int _recentHistoryTitles;
    [ObservableProperty] private bool _hasMonthlyHistory;

    [ObservableProperty] private bool _isHistoryPopupOpen;
    [ObservableProperty] private string _historyPopupTitle = string.Empty;
    [ObservableProperty] private string _historyPopupSubtitle = string.Empty;

    public ObservableCollection<AnalyticsHistoryEntry> HistoryPopupEntries { get; } = new();

    public void Refresh(IReadOnlyCollection<HistoryItem> history, IReadOnlyCollection<AnimeItem> items, IReadOnlyCollection<AnimeItem> completed)
    {
        RecentHistory.Clear();
        MonthlyHistory.Clear();
        YearDistribution.Clear();
        ReleaseYearCompletions.Clear();

        if (items.Count == 0) return;

        AddRecentHistory(history, items);
        AddMonthlyHistory(completed);
        AddYearDistribution(completed);
        AddReleaseYearCompletions(completed);
    }

    private void AddRecentHistory(IEnumerable<HistoryItem> history, IReadOnlyCollection<AnimeItem> items)
    {
        var today = DateTime.Now.Date;
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

    private void AddMonthlyHistory(IEnumerable<AnimeItem> completed)
    {
        var monthGroups = completed
            .Where(x => x.DateCompleted.HasValue && x.DateCompleted.Value.Year > 1900)
            .GroupBy(x => new { x.DateCompleted!.Value.Year, x.DateCompleted.Value.Month })
            .ToDictionary(x => (x.Key.Year, x.Key.Month), x => x.ToList());

        HasMonthlyHistory = monthGroups.Count > 0;
        if (!HasMonthlyHistory) return;

        var max = Math.Max(1, monthGroups.Values.Max(x => x.Count));
        var now = DateTime.Now;
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

                foreach (var entry in entries?.OrderBy(x => x.Presentation.DisplayTitle) ?? Enumerable.Empty<AnimeItem>())
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

    [RelayCommand]
    public void OpenFavorite(AnalyticsFavoriteRow? row)
    {
        if (row == null || row.Entries.Count == 0) return;

        HistoryPopupTitle = row.Name;
        HistoryPopupSubtitle = $"{row.Count} тайтл. • средняя {row.MeanScore} • вес {row.WeightedScore}";
        ShowHistoryPopup(row.Entries);
    }

    [RelayCommand]
    public void OpenDailyHistory(AnalyticsDailyHistoryPoint? point)
    {
        if (point == null || point.Count == 0) return;

        HistoryPopupTitle = point.DaysAgo == 1
            ? "Вчера"
            : $"{point.DaysAgo} дн. назад";
        HistoryPopupSubtitle = $"{point.DateLabel} · {point.Count} эп. · {point.Entries.Select(x => x.Title).Distinct().Count()} тайтл(ов)";
        ShowHistoryPopup(point.Entries);
    }

    [RelayCommand]
    public void OpenMonthlyHistory(AnalyticsMonthlyHistoryCell? cell)
    {
        if (cell == null || cell.Count == 0) return;

        HistoryPopupTitle = $"{cell.MonthName} · завершено";
        HistoryPopupSubtitle = $"{cell.Count} тайтл(ов)";
        ShowHistoryPopup(cell.Entries);
    }

    [RelayCommand]
    public void CloseHistoryPopup()
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
                ShareText = $"{group.Count:N0} тайтл.",
                Accent = $"#{alpha:X2}2D7DD2",
                TextColor = intensity >= 0.48 ? "#FFFFFFFF" : "#FF1F2937"
            });
        }
    }
}

