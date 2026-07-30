using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.ViewModels.AnimeList;

public sealed record ReleaseMapItem(string Title, AnimeItem Item, DateTime ReleaseAt, string Kind, string Note, string? PosterUrl);

public sealed record ReleaseMapDayGroup(DateTime Date, string Label, IReadOnlyList<ReleaseMapItem> Releases);

public class ReleaseMapViewModel
{
    private readonly IEnumerable<AnimeItem> _animeItems;

    public ReleaseMapViewModel(IEnumerable<AnimeItem> animeItems)
    {
        _animeItems = animeItems;
    }

    public IEnumerable<ReleaseMapItem> GetUpcomingReleases()
    {
        var now = DateTime.UtcNow;
        return _animeItems
            .Select(item => CreateReleaseMapItem(item, now))
            .Where(item => item != null)
            .Select(item => item!)
            .OrderBy(item => item.ReleaseAt);
    }

    public IEnumerable<ReleaseMapDayGroup> GetUpcomingReleaseGroups(int maxItems = 24)
    {
        var releases = GetUpcomingReleases().Take(maxItems).ToList();
        var groups = new List<ReleaseMapDayGroup>();
        var culture = GetReleaseCulture();
        var today = DateTime.Today;

        foreach (var group in releases.GroupBy(r => r.ReleaseAt.ToLocalTime().Date))
        {
            var date = group.Key;
            string label = date == today ? "Сегодня"
                : date == today.AddDays(1) ? "Завтра"
                : date.ToString("d MMMM, dddd", culture);
            
            groups.Add(new ReleaseMapDayGroup(date, label.ToUpper(culture), group.ToList()));
        }

        return groups;
    }

    private static ReleaseMapItem? CreateReleaseMapItem(AnimeItem item, DateTime now)
    {
        if (item.Status == UserAnimeStatus.Dropped)
            return null;

        if (item.NextEpisodeAt.HasValue && item.NextEpisodeAt.Value >= now.AddMinutes(-10))
        {
            var nextEpisode = item.EpisodesAired + 1;
            if (item.TotalEpisodes > 0)
                nextEpisode = Math.Min(nextEpisode, item.TotalEpisodes);
            return new ReleaseMapItem(
                GetPrimaryReleaseTitle(item),
                item,
                item.NextEpisodeAt.Value,
                nextEpisode > 0 ? $"{nextEpisode} серия" : "следующая серия",
                item.Presentation.AiringBadgeText,
                item.MainPictureUrl);
        }

        if (item.AiringDate.HasValue && item.AiringDate.Value >= now.Date)
        {
            return new ReleaseMapItem(
                GetPrimaryReleaseTitle(item),
                item,
                item.AiringDate.Value,
                "премьера",
                item.Season,
                item.MainPictureUrl);
        }

        return null;
    }

    private static string GetPrimaryReleaseTitle(AnimeItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.EnglishTitle))
            return item.EnglishTitle;

        return !string.IsNullOrWhiteSpace(item.Title) ? item.Title : item.Presentation.DisplayTitle;
    }

    public static string FormatRelativeDate(DateTime releaseAt)
    {
        var today = DateTime.Today;
        var date = releaseAt.ToLocalTime().Date;
        if (date == today) return "сегодня";
        if (date == today.AddDays(1)) return "завтра";
        var diff = (date - today).Days;
        return diff > 1 ? $"через {diff} дн." : releaseAt.ToLocalTime().ToString("dd MMM", CultureInfo.CurrentCulture);
    }

    public static string FormatBadgeDate(DateTime releaseAt)
    {
        var today = DateTime.Today;
        var date = releaseAt.ToLocalTime().Date;
        if (date == today)
            return "Сегодня";
        if (date == today.AddDays(1))
            return "Завтра";

        var diff = (date - today).Days;
        if (diff > 0)
            return $"{diff} дн.";

        return releaseAt.ToLocalTime().ToString("dd MMM", CultureInfo.CurrentCulture);
    }

    public static string FormatMonthShort(DateTime date)
    {
        var culture = GetReleaseCulture();
        return date.ToLocalTime().ToString("MMM", culture).TrimEnd('.').ToUpper(culture);
    }

    public static CultureInfo GetReleaseCulture()
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"
            ? CultureInfo.GetCultureInfo("ru-RU")
            : CultureInfo.CurrentCulture;
    }

    public static string GetHeroReleaseKind(ReleaseMapItem release)
    {
        return release.Kind.Contains("премьера", StringComparison.OrdinalIgnoreCase)
            ? "Премьера"
            : release.Kind;
    }

    public static string FormatWeekReleaseSummary(IReadOnlyCollection<ReleaseMapItem> releases)
    {
        var end = DateTime.UtcNow.AddDays(7);
        var count = releases.Count(release => release.ReleaseAt < end);
        return $"{count} {PluralizeRelease(count)} на этой неделе";
    }

    public static string PluralizeRelease(int count)
    {
        var lastTwo = count % 100;
        if (lastTwo is >= 11 and <= 14)
            return "релизов";

        return (count % 10) switch
        {
            1 => "релиз",
            >= 2 and <= 4 => "релиза",
            _ => "релизов"
        };
    }

    public static string FormatUntilRelease(DateTime releaseAt)
    {
        var diff = releaseAt - DateTime.UtcNow;
        if (diff.TotalMinutes <= 1)
            return "сейчас";

        if (diff.TotalDays >= 1)
        {
            var days = (int)Math.Floor(diff.TotalDays);
            var hours = diff.Hours;
            return hours > 0 ? $"{days}д {hours}ч" : $"{days}д";
        }

        if (diff.TotalHours >= 1)
            return $"{(int)Math.Floor(diff.TotalHours)}ч {diff.Minutes}м";

        return $"{Math.Max(1, diff.Minutes)}м";
    }
}
