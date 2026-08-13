using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kiriha.Localization;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Kiriha.Core.Abstractions.Models.Entities;

namespace Kiriha.ViewModels.AnimeList;

public sealed record ReleaseMapItem(string Title, AnimeEntity Item, DateTime ReleaseAt, string Kind, string Note, string? PosterUrl);

public sealed record ReleaseMapDayGroup(DateTime Date, string Label, IReadOnlyList<ReleaseMapItem> Releases);

public class ReleaseMapViewModel
{
    private readonly IEnumerable<AnimeEntity> _animeItems;

    public ReleaseMapViewModel(IEnumerable<AnimeEntity> animeItems)
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
            string label = date == today ? LocalizationStore.Translate("schedule.today")
                : date == today.AddDays(1) ? LocalizationStore.Translate("schedule.tomorrow")
                : date.ToString("d MMMM, dddd", culture);
            
            groups.Add(new ReleaseMapDayGroup(date, label.ToUpper(culture), group.ToList()));
        }

        return groups;
    }

    private static ReleaseMapItem? CreateReleaseMapItem(AnimeEntity item, DateTime now)
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
                nextEpisode > 0 ? string.Format(LocalizationStore.Translate("schedule.episode_format"), nextEpisode) : LocalizationStore.Translate("schedule.next_episode"),
                item.Presentation.AiringBadgeText,
                item.MainPictureUrl);
        }

        if (item.AiringDate.HasValue && item.AiringDate.Value >= now.Date)
        {
            return new ReleaseMapItem(
                GetPrimaryReleaseTitle(item),
                item,
                item.AiringDate.Value,
                LocalizationStore.Translate("schedule.premiere_lower"),
                item.Season,
                item.MainPictureUrl);
        }

        return null;
    }

    private static string GetPrimaryReleaseTitle(AnimeEntity item)
    {
        if (!string.IsNullOrWhiteSpace(item.EnglishTitle))
            return item.EnglishTitle;

        return !string.IsNullOrWhiteSpace(item.Title) ? item.Title : item.Presentation.DisplayTitle;
    }

    public static string FormatRelativeDate(DateTime releaseAt)
    {
        var today = DateTime.Today;
        var date = releaseAt.ToLocalTime().Date;
        if (date == today) return LocalizationStore.Translate("schedule.today").ToLower();
        if (date == today.AddDays(1)) return LocalizationStore.Translate("schedule.tomorrow").ToLower();
        var diff = (date - today).Days;
        return diff > 1 ? string.Format(LocalizationStore.Translate("schedule.days_later"), diff) : releaseAt.ToLocalTime().ToString("dd MMM", CultureInfo.CurrentCulture);
    }

    public static string FormatBadgeDate(DateTime releaseAt)
    {
        var today = DateTime.Today;
        var date = releaseAt.ToLocalTime().Date;
        if (date == today)
            return LocalizationStore.Translate("schedule.today");
        if (date == today.AddDays(1))
            return LocalizationStore.Translate("schedule.tomorrow");

        var diff = (date - today).Days;
        if (diff > 0)
            return string.Format(LocalizationStore.Translate("schedule.days_abbrev"), diff);

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
        return release.Kind.Contains(LocalizationStore.Translate("schedule.premiere_lower"), StringComparison.OrdinalIgnoreCase)
            ? LocalizationStore.Translate("schedule.premiere")
            : release.Kind;
    }

    public static string FormatWeekReleaseSummary(IReadOnlyCollection<ReleaseMapItem> releases)
    {
        var end = DateTime.UtcNow.AddDays(7);
        var count = releases.Count(release => release.ReleaseAt < end);
        return string.Format(LocalizationStore.Translate("schedule.releases_this_week"), count, PluralizeRelease(count));
    }

    public static string PluralizeRelease(int count)
    {
        var culture = GetReleaseCulture();
        if (culture.TwoLetterISOLanguageName != "ru")
            return count == 1 ? LocalizationStore.Translate("schedule.release_1") : LocalizationStore.Translate("schedule.release_5_0");

        var lastTwo = count % 100;
        if (lastTwo is >= 11 and <= 14)
            return LocalizationStore.Translate("schedule.release_5_0");

        return (count % 10) switch
        {
            1 => LocalizationStore.Translate("schedule.release_1"),
            >= 2 and <= 4 => LocalizationStore.Translate("schedule.release_2_4"),
            _ => LocalizationStore.Translate("schedule.release_5_0")
        };
    }

    public static string FormatUntilRelease(DateTime releaseAt)
    {
        var diff = releaseAt - DateTime.UtcNow;
        if (diff.TotalMinutes <= 1)
            return LocalizationStore.Translate("schedule.now");

        if (diff.TotalDays >= 1)
        {
            var days = (int)Math.Floor(diff.TotalDays);
            var hours = diff.Hours;
            return hours > 0 ? string.Format(LocalizationStore.Translate("schedule.days_hours"), days, hours) : string.Format(LocalizationStore.Translate("schedule.days_only"), days);
        }

        if (diff.TotalHours >= 1)
            return string.Format(LocalizationStore.Translate("schedule.hours_minutes"), (int)Math.Floor(diff.TotalHours), diff.Minutes);

        return string.Format(LocalizationStore.Translate("schedule.minutes_only"), Math.Max(1, diff.Minutes));
    }
}
