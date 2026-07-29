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
}
