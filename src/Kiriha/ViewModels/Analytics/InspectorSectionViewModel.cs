using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Dialogs;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking;
using Kiriha.Localization;
using Kiriha.Models;

namespace Kiriha.ViewModels.Analytics;

public enum InspectorFilterCategory
{
    All,
    Dates,
    Scores,
    StatusAndProgress
}

public enum ProfileIssueType
{
    MissingEndDate,
    MissingStartDate,
    MissingScore,
    EpisodesFinished,
    ProgressOverflow,
    StaleWatching
}

public sealed class ProfileIssueItem : ObservableObject
{
    public required AnimeEntity Anime { get; init; }
    public required ProfileIssueType IssueType { get; init; }
    public required InspectorFilterCategory Category { get; init; }
    public required string Title { get; init; }
    public string? PosterUrl => Anime.MainPictureUrl;
    public required string StatusText { get; init; }
    public required string StatusAccent { get; init; }
    public required string IssueTitle { get; init; }
    public required string IssueDescription { get; init; }
    public required string ActionLabel { get; init; }
    public required string AccentColor { get; init; }
    public required string IconKind { get; init; }
}

public partial class InspectorSectionViewModel : ViewModelBase
{
    private readonly IAnimeRepository _animeRepo;
    private readonly ISyncManager _syncManager;
    private readonly IDialogService _dialogService;
    private readonly ILocalizer _localizer;
    private readonly Kiriha.Core.Abstractions.Services.Tracking.IMalHistoryDeepParserService _parserService;

    public ObservableCollection<ProfileIssueItem> Issues { get; } = new();
    public ObservableCollection<ProfileIssueItem> FilteredIssues { get; } = new();

    [ObservableProperty] private int _healthScore = 100;
    [ObservableProperty] private string _healthStatusText = string.Empty;
    [ObservableProperty] private string _healthStatusColor = "#FF2E9D62";
    [ObservableProperty] private int _issuesCount;
    [ObservableProperty] private int _missingDatesCount;
    [ObservableProperty] private int _missingScoresCount;
    [ObservableProperty] private int _statusMismatchCount;
    [ObservableProperty] private InspectorFilterCategory _selectedCategory = InspectorFilterCategory.All;
    [ObservableProperty] private bool _hasIssues;
    [ObservableProperty] private bool _isBatchOperating;
    [ObservableProperty] private string? _statusMessage;

    public bool IsFilterAll => SelectedCategory == InspectorFilterCategory.All;
    public bool IsFilterDates => SelectedCategory == InspectorFilterCategory.Dates;
    public bool IsFilterScores => SelectedCategory == InspectorFilterCategory.Scores;
    public bool IsFilterStatus => SelectedCategory == InspectorFilterCategory.StatusAndProgress;

    public InspectorSectionViewModel(
        IAnimeRepository animeRepo,
        ISyncManager syncManager,
        IDialogService dialogService,
        ILocalizer localizer,
        Kiriha.Core.Abstractions.Services.Tracking.IMalHistoryDeepParserService parserService)
    {
        _animeRepo = animeRepo;
        _syncManager = syncManager;
        _dialogService = dialogService;
        _localizer = localizer;
        _parserService = parserService;
    }

    [RelayCommand]
    public async Task OpenDeepParser()
    {
        var vm = new MalDeepParserViewModel(_animeRepo, _syncManager, _parserService, _localizer);
        var win = new Views.Analytics.MalDeepParserWindow(vm);

        var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWindow != null)
        {
            await win.ShowDialog(mainWindow);
        }
        else
        {
            win.Show();
        }

        Refresh(_animeRepo.Collection.ToList());
    }

    public void Refresh(IReadOnlyCollection<AnimeEntity> items)
    {
        Issues.Clear();

        if (items.Count == 0)
        {
            HealthScore = 100;
            HealthStatusText = _localizer.GetLoc("analytics.inspector.health_excellent");
            HealthStatusColor = "#FF2E9D62";
            IssuesCount = 0;
            MissingDatesCount = 0;
            MissingScoresCount = 0;
            StatusMismatchCount = 0;
            HasIssues = false;
            ApplyFilter();
            return;
        }

        var uniqueProblemAnimeIds = new HashSet<int>();

        foreach (var item in items)
        {
            // 1. Missing end date: Completed but DateCompleted is null
            if (item.Status == UserAnimeStatus.Completed && item.DateCompleted == null)
            {
                uniqueProblemAnimeIds.Add(item.Id);
                Issues.Add(new ProfileIssueItem
                {
                    Anime = item,
                    IssueType = ProfileIssueType.MissingEndDate,
                    Category = InspectorFilterCategory.Dates,
                    Title = item.RussianTitle ?? item.Title,
                    StatusText = AnalyticsHelpers.GetStatusLabel(item.Status, _localizer),
                    StatusAccent = AnalyticsHelpers.GetStatusAccent(item.Status),
                    IssueTitle = _localizer.GetLoc("analytics.inspector.issue_missing_end_date"),
                    IssueDescription = _localizer.GetLoc("analytics.inspector.issue_missing_end_date_desc"),
                    ActionLabel = _localizer.GetLoc("analytics.inspector.action_set_today"),
                    AccentColor = "#FFD17A22", // Amber
                    IconKind = "CalendarCheckOutline"
                });
            }

            // 2. Missing start date: Watching or Completed but DateStarted is null
            if ((item.Status == UserAnimeStatus.Watching || item.Status == UserAnimeStatus.Completed) && item.DateStarted == null)
            {
                uniqueProblemAnimeIds.Add(item.Id);
                Issues.Add(new ProfileIssueItem
                {
                    Anime = item,
                    IssueType = ProfileIssueType.MissingStartDate,
                    Category = InspectorFilterCategory.Dates,
                    Title = item.RussianTitle ?? item.Title,
                    StatusText = AnalyticsHelpers.GetStatusLabel(item.Status, _localizer),
                    StatusAccent = AnalyticsHelpers.GetStatusAccent(item.Status),
                    IssueTitle = _localizer.GetLoc("analytics.inspector.issue_missing_start_date"),
                    IssueDescription = _localizer.GetLoc("analytics.inspector.issue_missing_start_date_desc"),
                    ActionLabel = _localizer.GetLoc("analytics.inspector.action_set_today"),
                    AccentColor = "#FFD17A22", // Amber
                    IconKind = "CalendarToday"
                });
            }

            // 3. Completed without score
            if (item.Status == UserAnimeStatus.Completed && (string.IsNullOrEmpty(item.Score) || item.Score == "-" || item.Score == "0"))
            {
                uniqueProblemAnimeIds.Add(item.Id);
                Issues.Add(new ProfileIssueItem
                {
                    Anime = item,
                    IssueType = ProfileIssueType.MissingScore,
                    Category = InspectorFilterCategory.Scores,
                    Title = item.RussianTitle ?? item.Title,
                    StatusText = AnalyticsHelpers.GetStatusLabel(item.Status, _localizer),
                    StatusAccent = AnalyticsHelpers.GetStatusAccent(item.Status),
                    IssueTitle = _localizer.GetLoc("analytics.inspector.issue_missing_score"),
                    IssueDescription = _localizer.GetLoc("analytics.inspector.issue_missing_score_desc"),
                    ActionLabel = _localizer.GetLoc("analytics.inspector.action_details"),
                    AccentColor = "#FF7B61FF", // Violet
                    IconKind = "StarOutline"
                });
            }

            // 4. Progress >= TotalEpisodes (where TotalEpisodes > 0) but not marked Completed
            if (item.Status != UserAnimeStatus.Completed && item.Status != UserAnimeStatus.Dropped &&
                item.TotalEpisodes > 0 && item.Progress >= item.TotalEpisodes)
            {
                uniqueProblemAnimeIds.Add(item.Id);
                Issues.Add(new ProfileIssueItem
                {
                    Anime = item,
                    IssueType = ProfileIssueType.EpisodesFinished,
                    Category = InspectorFilterCategory.StatusAndProgress,
                    Title = item.RussianTitle ?? item.Title,
                    StatusText = AnalyticsHelpers.GetStatusLabel(item.Status, _localizer),
                    StatusAccent = AnalyticsHelpers.GetStatusAccent(item.Status),
                    IssueTitle = _localizer.GetLoc("analytics.inspector.issue_episodes_finished"),
                    IssueDescription = string.Format(_localizer.GetLoc("analytics.inspector.issue_episodes_finished_desc"), item.TotalEpisodes, AnalyticsHelpers.GetStatusLabel(item.Status, _localizer)),
                    ActionLabel = _localizer.GetLoc("analytics.inspector.action_set_completed"),
                    AccentColor = "#FF00897B", // Teal
                    IconKind = "CheckAll"
                });
            }

            // 5. Progress overflow: Progress > TotalEpisodes
            if (item.TotalEpisodes > 0 && item.Progress > item.TotalEpisodes)
            {
                uniqueProblemAnimeIds.Add(item.Id);
                Issues.Add(new ProfileIssueItem
                {
                    Anime = item,
                    IssueType = ProfileIssueType.ProgressOverflow,
                    Category = InspectorFilterCategory.StatusAndProgress,
                    Title = item.RussianTitle ?? item.Title,
                    StatusText = AnalyticsHelpers.GetStatusLabel(item.Status, _localizer),
                    StatusAccent = AnalyticsHelpers.GetStatusAccent(item.Status),
                    IssueTitle = _localizer.GetLoc("analytics.inspector.issue_progress_overflow"),
                    IssueDescription = string.Format(_localizer.GetLoc("analytics.inspector.issue_progress_overflow_desc"), item.Progress, item.TotalEpisodes),
                    ActionLabel = _localizer.GetLoc("analytics.inspector.action_fix"),
                    AccentColor = "#FFE53935", // Red
                    IconKind = "AlertCircleOutline"
                });
            }

            // 6. Stale watching: Status is Watching, but Progress == 0
            if (item.Status == UserAnimeStatus.Watching && item.Progress == 0 && item.ChaptersRead == 0)
            {
                uniqueProblemAnimeIds.Add(item.Id);
                Issues.Add(new ProfileIssueItem
                {
                    Anime = item,
                    IssueType = ProfileIssueType.StaleWatching,
                    Category = InspectorFilterCategory.StatusAndProgress,
                    Title = item.RussianTitle ?? item.Title,
                    StatusText = AnalyticsHelpers.GetStatusLabel(item.Status, _localizer),
                    StatusAccent = AnalyticsHelpers.GetStatusAccent(item.Status),
                    IssueTitle = _localizer.GetLoc("analytics.inspector.issue_stale_watching"),
                    IssueDescription = _localizer.GetLoc("analytics.inspector.issue_stale_watching_desc"),
                    ActionLabel = _localizer.GetLoc("analytics.inspector.action_details"),
                    AccentColor = "#FF5C80BC", // Blue
                    IconKind = "Sleep"
                });
            }
        }

        IssuesCount = Issues.Count;
        MissingDatesCount = Issues.Count(x => x.Category == InspectorFilterCategory.Dates);
        MissingScoresCount = Issues.Count(x => x.Category == InspectorFilterCategory.Scores);
        StatusMismatchCount = Issues.Count(x => x.Category == InspectorFilterCategory.StatusAndProgress);
        HasIssues = Issues.Count > 0;

        // Health Score calculation (0 to 100%)
        var cleanTitles = Math.Max(0, items.Count - uniqueProblemAnimeIds.Count);
        HealthScore = items.Count > 0 ? (int)Math.Round(cleanTitles * 100.0 / items.Count) : 100;

        if (HealthScore >= 95)
        {
            HealthStatusText = _localizer.GetLoc("analytics.inspector.health_excellent");
            HealthStatusColor = "#FF2E9D62";
        }
        else if (HealthScore >= 80)
        {
            HealthStatusText = _localizer.GetLoc("analytics.inspector.health_good");
            HealthStatusColor = "#FF2D7DD2";
        }
        else
        {
            HealthStatusText = _localizer.GetLoc("analytics.inspector.health_needs_attention");
            HealthStatusColor = "#FFD17A22";
        }

        ApplyFilter();
    }

    [RelayCommand]
    public void SetFilterCategory(InspectorFilterCategory category)
    {
        SelectedCategory = category;
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterDates));
        OnPropertyChanged(nameof(IsFilterScores));
        OnPropertyChanged(nameof(IsFilterStatus));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredIssues.Clear();
        var items = SelectedCategory switch
        {
            InspectorFilterCategory.Dates => Issues.Where(x => x.Category == InspectorFilterCategory.Dates),
            InspectorFilterCategory.Scores => Issues.Where(x => x.Category == InspectorFilterCategory.Scores),
            InspectorFilterCategory.StatusAndProgress => Issues.Where(x => x.Category == InspectorFilterCategory.StatusAndProgress),
            _ => (IEnumerable<ProfileIssueItem>)Issues
        };

        foreach (var item in items)
        {
            FilteredIssues.Add(item);
        }
    }

    [RelayCommand]
    public async Task FixIssue(ProfileIssueItem item)
    {
        if (item == null) return;

        bool updated = false;
        var anime = item.Anime;

        switch (item.IssueType)
        {
            case ProfileIssueType.MissingEndDate:
                anime.DateCompleted = DateTime.Today;
                updated = true;
                break;

            case ProfileIssueType.MissingStartDate:
                anime.DateStarted = DateTime.Today;
                updated = true;
                break;

            case ProfileIssueType.EpisodesFinished:
                anime.Status = UserAnimeStatus.Completed;
                anime.DateCompleted ??= DateTime.Today;
                updated = true;
                break;

            case ProfileIssueType.ProgressOverflow:
                if (anime.TotalEpisodes > 0)
                {
                    anime.Progress = anime.TotalEpisodes;
                    updated = true;
                }
                break;

            case ProfileIssueType.MissingScore:
            case ProfileIssueType.StaleWatching:
                // Open dialog for interactive decision
                await OpenDetails(item);
                return;
        }

        if (updated)
        {
            await _animeRepo.AddOrUpdateAnimeAsync(anime);
            await _syncManager.EnqueueFullUpdateAsync(anime);
            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());

            Issues.Remove(item);
            FilteredIssues.Remove(item);
            IssuesCount = Issues.Count;
            MissingDatesCount = Issues.Count(x => x.Category == InspectorFilterCategory.Dates);
            MissingScoresCount = Issues.Count(x => x.Category == InspectorFilterCategory.Scores);
            StatusMismatchCount = Issues.Count(x => x.Category == InspectorFilterCategory.StatusAndProgress);
            HasIssues = Issues.Count > 0;
        }
    }

    [RelayCommand]
    public async Task OpenDetails(ProfileIssueItem item)
    {
        if (item == null) return;
        await _dialogService.ShowAnimeDetailsAsync(null, item.Anime);
    }

    [RelayCommand]
    public async Task BatchFixCompletedDates()
    {
        if (IsBatchOperating) return;

        var missingEndDateIssues = Issues
            .Where(x => x.IssueType == ProfileIssueType.MissingEndDate)
            .ToList();

        if (missingEndDateIssues.Count == 0) return;

        try
        {
            IsBatchOperating = true;
            int count = 0;

            foreach (var issue in missingEndDateIssues)
            {
                issue.Anime.DateCompleted = DateTime.Today;
                await _animeRepo.AddOrUpdateAnimeAsync(issue.Anime);
                await _syncManager.EnqueueFullUpdateAsync(issue.Anime);
                Issues.Remove(issue);
                FilteredIssues.Remove(issue);
                count++;
            }

            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
            IssuesCount = Issues.Count;
            MissingDatesCount = Issues.Count(x => x.Category == InspectorFilterCategory.Dates);
            HasIssues = Issues.Count > 0;

            StatusMessage = string.Format(_localizer.GetLoc("analytics.inspector.batch_success_end_dates"), count);
        }
        finally
        {
            IsBatchOperating = false;
        }
    }

    [RelayCommand]
    public async Task BatchFixFinishedStatus()
    {
        if (IsBatchOperating) return;

        var finishedIssues = Issues
            .Where(x => x.IssueType == ProfileIssueType.EpisodesFinished)
            .ToList();

        if (finishedIssues.Count == 0) return;

        try
        {
            IsBatchOperating = true;
            int count = 0;

            foreach (var issue in finishedIssues)
            {
                issue.Anime.Status = UserAnimeStatus.Completed;
                issue.Anime.DateCompleted ??= DateTime.Today;
                await _animeRepo.AddOrUpdateAnimeAsync(issue.Anime);
                await _syncManager.EnqueueFullUpdateAsync(issue.Anime);
                Issues.Remove(issue);
                FilteredIssues.Remove(issue);
                count++;
            }

            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
            IssuesCount = Issues.Count;
            StatusMismatchCount = Issues.Count(x => x.Category == InspectorFilterCategory.StatusAndProgress);
            HasIssues = Issues.Count > 0;

            StatusMessage = string.Format(_localizer.GetLoc("analytics.inspector.batch_success_finished"), count);
        }
        finally
        {
            IsBatchOperating = false;
        }
    }
}
