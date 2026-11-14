using System.Collections.ObjectModel;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.ViewModels.Analytics;

public partial class ReadNextSectionViewModel : ViewModelBase
{
    private readonly ILocalizer _localizer;

    public ReadNextSectionViewModel(ILocalizer localizer)
    {
        _localizer = localizer;
    }

    public ObservableCollection<ProfileTodoItem> ReadTodo { get; } = new();
    public ObservableCollection<ProfileTodoItem> FinishedReadTodo { get; } = new();
    public ObservableCollection<ProfileTodoItem> UpcomingTodo { get; } = new();
    public ObservableCollection<ProfileTodoItem> PlanTodo { get; } = new();
    public ObservableCollection<ProfileTodoItem> StaleTodo { get; } = new();

    public void Refresh(IReadOnlyCollection<AnimeEntity> items)
    {
        ReadTodo.Clear();
        FinishedReadTodo.Clear();
        UpcomingTodo.Clear();
        PlanTodo.Clear();
        StaleTodo.Clear();

        if (items.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var item in items
                     .Where(x => IsCurrentOngoing(x, now)
                                 && x.Status == UserAnimeStatus.Watching
                                 && GetUnreadChaptersCount(x) > 0)
                     .OrderByDescending(GetUnreadChaptersCount)
                     .ThenByDescending(x => x.NextEpisodeAt ?? DateTime.MinValue)
                     .Take(6))
        {
            var unread = GetUnreadChaptersCount(item);
            var read = GetChaptersProgress(item);
            ReadTodo.Add(ToTodo(
                item,
                _localizer.GetLoc("analytics.read_next.chapters_format", unread),
                _localizer.GetLoc("analytics.read_next.read_progress", read, DisplayTotal(item)),
                "#FFE53935"));
        }

        foreach (var item in items
                     .Where(x => !IsCurrentOngoing(x, now)
                                 && x.Status == UserAnimeStatus.Watching
                                 && GetUnreadChaptersCount(x) > 0)
                     .OrderByDescending(GetUnreadChaptersCount)
                     .ThenByDescending(ParsedMeanScore)
                     .Take(6))
        {
            var unread = GetUnreadChaptersCount(item);
            var read = GetChaptersProgress(item);
            FinishedReadTodo.Add(ToTodo(
                item,
                _localizer.GetLoc("analytics.read_next.chapters_format", unread),
                _localizer.GetLoc("analytics.read_next.read_progress", read, DisplayTotal(item)),
                "#FF7B61FF"));
        }

        foreach (var item in items
                     .Where(x => x.NextEpisodeAt.HasValue && x.NextEpisodeAt.Value >= now)
                     .OrderBy(x => x.NextEpisodeAt)
                     .Take(6))
        {
            UpcomingTodo.Add(ToTodo(
                item,
                item.Presentation.AiringBadgeText,
                FormatUpcomingDetail(item),
                "#FF2D7DD2"));
        }

        foreach (var item in items
                     .Where(x => x.Status == UserAnimeStatus.PlanToWatch)
                     .OrderByDescending(ParsedMeanScore)
                     .ThenBy(x => x.Popularity == 0 ? int.MaxValue : x.Popularity)
                     .Take(6))
        {
            PlanTodo.Add(ToTodo(
                item,
                item.MeanScore ?? "-",
                FormatPlanDetail(item),
                "#FF2E9D62"));
        }

        foreach (var item in items
                     .Where(x => x.Status == UserAnimeStatus.Watching && GetChaptersProgress(x) > 0 && !IsCurrentOngoing(x, now))
                     .OrderBy(x => x.DateStarted ?? DateTime.MaxValue)
                     .ThenBy(x => x.LastEpisodeAt ?? DateTime.MaxValue)
                     .Take(6))
        {
            var pauseFrom = item.DateStarted ?? item.LastEpisodeAt ?? now;
            var days = Math.Max(1, (int)(now - pauseFrom).TotalDays);
            var read = GetChaptersProgress(item);
            StaleTodo.Add(ToTodo(
                item,
                _localizer.GetLoc("analytics.read_next.days_format", days),
                _localizer.GetLoc("analytics.read_next.paused_on", read, DisplayTotal(item)),
                "#FFD17A22"));
        }
    }


}


