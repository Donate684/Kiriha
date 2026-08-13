using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.ViewModels.Analytics;

public partial class WatchNextSectionViewModel : ViewModelBase
{
    public ObservableCollection<ProfileTodoItem> WatchTodo { get; } = new();
    public ObservableCollection<ProfileTodoItem> FinishedWatchTodo { get; } = new();
    public ObservableCollection<ProfileTodoItem> UpcomingTodo { get; } = new();
    public ObservableCollection<ProfileTodoItem> PlanTodo { get; } = new();
    public ObservableCollection<ProfileTodoItem> StaleTodo { get; } = new();

    public void Refresh(IReadOnlyCollection<AnimeEntity> items)
    {
        WatchTodo.Clear();
        FinishedWatchTodo.Clear();
        UpcomingTodo.Clear();
        PlanTodo.Clear();
        StaleTodo.Clear();

        if (items.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var item in items
                     .Where(x => IsCurrentOngoing(x, now) && x.Presentation.ShowAiredProgressBar && x.Presentation.UnseenEpisodesCount > 0)
                     .OrderByDescending(x => x.Presentation.UnseenEpisodesCount)
                     .ThenByDescending(x => x.NextEpisodeAt ?? DateTime.MinValue)
                     .Take(6))
        {
            WatchTodo.Add(ToTodo(
                item,
                $"{item.Presentation.UnseenEpisodesCount} эп.",
                $"Просмотрено {item.Progress}/{DisplayTotal(item)}",
                "#FFE53935"));
        }

        foreach (var item in items
                     .Where(x => !IsCurrentOngoing(x, now)
                                 && x.Status == UserAnimeStatus.Watching
                                 && x.Presentation.ShowAiredProgressBar
                                 && x.Presentation.UnseenEpisodesCount > 0)
                     .OrderByDescending(x => x.Presentation.UnseenEpisodesCount)
                     .ThenByDescending(ParsedMeanScore)
                     .Take(6))
        {
            FinishedWatchTodo.Add(ToTodo(
                item,
                $"{item.Presentation.UnseenEpisodesCount} эп.",
                $"Просмотрено {item.Progress}/{DisplayTotal(item)}",
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
                     .Where(x => x.Status == UserAnimeStatus.Watching && x.Progress > 0 && !IsCurrentOngoing(x, now))
                     .OrderBy(x => x.DateStarted ?? DateTime.MaxValue)
                     .ThenBy(x => x.LastEpisodeAt ?? DateTime.MaxValue)
                     .Take(6))
        {
            var pauseFrom = item.DateStarted ?? item.LastEpisodeAt ?? now;
            var days = Math.Max(1, (int)(now - pauseFrom).TotalDays);
            StaleTodo.Add(ToTodo(
                item,
                $"{days} дн.",
                $"Пауза на {item.Progress}/{DisplayTotal(item)}",
                "#FFD17A22"));
        }
    }


}

