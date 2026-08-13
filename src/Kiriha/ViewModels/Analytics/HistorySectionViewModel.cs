using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Localization;
using Kiriha.Core.Abstractions.Models.Entities;

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

    public void Refresh(IReadOnlyCollection<HistoryItem> history, IReadOnlyCollection<AnimeEntity> items, IReadOnlyCollection<AnimeEntity> completed)
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
        HistoryPopupSubtitle = string.Format(LocalizationStore.Translate("analytics.history.popup_favorite_subtitle"), row.Count, row.MeanScore, row.WeightedScore);
        ShowHistoryPopup(row.Entries);
    }

    [RelayCommand]
    public void OpenDailyHistory(AnalyticsDailyHistoryPoint? point)
    {
        if (point == null || point.Count == 0) return;

        HistoryPopupTitle = point.DaysAgo == 1
            ? LocalizationStore.Translate("analytics.history.yesterday")
            : string.Format(LocalizationStore.Translate("analytics.history.days_ago_format"), point.DaysAgo);
        HistoryPopupSubtitle = string.Format(LocalizationStore.Translate("analytics.history.popup_daily_subtitle"), point.DateLabel, point.Count, point.Entries.Select(x => x.Title).Distinct().Count());
        ShowHistoryPopup(point.Entries);
    }

    [RelayCommand]
    public void OpenMonthlyHistory(AnalyticsMonthlyHistoryCell? cell)
    {
        if (cell == null || cell.Count == 0) return;

        HistoryPopupTitle = string.Format(LocalizationStore.Translate("analytics.history.popup_monthly_title"), cell.MonthName);
        HistoryPopupSubtitle = string.Format(LocalizationStore.Translate("analytics.history.popup_monthly_subtitle"), cell.Count);
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
