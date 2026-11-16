using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Metadata;

namespace Kiriha.ViewModels.Analytics;

public partial class HistorySectionViewModel : ViewModelBase
{
    private const int RecentHistoryDays = 14;
    private readonly ILocalizer _localizer;

    public HistorySectionViewModel(ILocalizer? localizer = null)
    {
        _localizer = localizer ?? LocalizationService.Active;
        _monthHeaders = GetLocalizedMonthHeaders(_localizer);
    }

    public ObservableCollection<AnalyticsDailyHistoryPoint> RecentHistory { get; } = new();
    public ObservableCollection<AnalyticsMonthlyHistoryRow> MonthlyHistory { get; } = new();
    public ObservableCollection<AnalyticsBar> YearDistribution { get; } = new();
    public ObservableCollection<AnalyticsBar> ReleaseYearCompletions { get; } = new();

    [ObservableProperty]
    private IReadOnlyList<string> _monthHeaders;

    [ObservableProperty] private int _recentHistoryEpisodes;
    [ObservableProperty] private int _recentHistoryTitles;
    [ObservableProperty] private bool _hasMonthlyHistory;

    [ObservableProperty] private bool _isHistoryPopupOpen;
    [ObservableProperty] private string _historyPopupTitle = string.Empty;
    [ObservableProperty] private string _historyPopupSubtitle = string.Empty;

    public ObservableCollection<AnalyticsHistoryEntry> HistoryPopupEntries { get; } = new();

    public static IReadOnlyList<string> GetLocalizedMonthHeaders(ILocalizer? localizer = null)
    {
        var loc = localizer ?? LocalizationService.Active;
        var raw = loc.GetLoc("analytics.history.months_short");
        if (!string.IsNullOrEmpty(raw) && raw.Contains(','))
        {
            var parts = raw.Split(',');
            if (parts.Length == 12)
            {
                return parts;
            }
        }

        return ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    }

    public void Refresh(IReadOnlyCollection<HistoryItem> history, IReadOnlyCollection<AnimeEntity> items, IReadOnlyCollection<AnimeEntity> completed)
    {
        MonthHeaders = GetLocalizedMonthHeaders(_localizer);
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
        if (row is null || row.Entries.Count == 0) return;

        HistoryPopupTitle = row.Name;
        HistoryPopupSubtitle = _localizer.GetLoc("analytics.history.popup_favorite_subtitle", row.Count, row.MeanScore, row.WeightedScore);
        ShowHistoryPopup(row.Entries);
    }

    [RelayCommand]
    public void OpenDailyHistory(AnalyticsDailyHistoryPoint? point)
    {
        if (point is null || point.Count == 0) return;

        HistoryPopupTitle = point.DaysAgo switch
        {
            0 => _localizer.GetLoc("analytics.history.today"),
            1 => _localizer.GetLoc("analytics.history.yesterday"),
            _ => _localizer.GetLoc("analytics.history.days_ago_format", point.DaysAgo)
        };
        HistoryPopupSubtitle = _localizer.GetLoc("analytics.history.popup_daily_subtitle", point.DateLabel, point.Count, point.Entries.Select(x => x.Title).Distinct().Count());
        ShowHistoryPopup(point.Entries);
    }

    [RelayCommand]
    public void OpenMonthlyHistory(AnalyticsMonthlyHistoryCell? cell)
    {
        if (cell is null || cell.Count == 0) return;

        HistoryPopupTitle = _localizer.GetLoc("analytics.history.popup_monthly_title", cell.MonthName);
        HistoryPopupSubtitle = _localizer.GetLoc("analytics.history.popup_monthly_subtitle", cell.Count);
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
