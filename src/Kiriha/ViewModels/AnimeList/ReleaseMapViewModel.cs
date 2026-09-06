using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kiriha.Localization;
using Kiriha.Models;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.ViewModels.AnimeList;

public enum ReleaseMapFilter
{
    Upcoming,
    Past
}

public sealed record ReleaseMapItem(string Title, AnimeEntity Item, DateTime ReleaseAt, string Kind, string Note, string? PosterUrl);

public sealed record ReleaseMapDayGroup(DateTime Date, string Label, IReadOnlyList<ReleaseMapItem> Releases);

public class ReleaseMapViewModel
{
    private static readonly Dictionary<string, string> FallbackStrings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["schedule.today"] = "Today",
        ["schedule.tomorrow"] = "Tomorrow",
        ["schedule.yesterday"] = "Yesterday",
        ["schedule.day_before_yesterday"] = "2 days ago",
        ["schedule.days_ago"] = "{0}d ago",
        ["schedule.hours_ago"] = "{0}h ago",
        ["schedule.minutes_ago"] = "{0}m ago",
        ["schedule.days_later"] = "in {0} d.",
        ["schedule.days_abbrev"] = "{0} d.",
        ["schedule.days_hours"] = "{0}d {1}h",
        ["schedule.days_only"] = "{0}d",
        ["schedule.hours_minutes"] = "{0}h {1}m",
        ["schedule.minutes_only"] = "{0}m",
        ["schedule.now"] = "now",
        ["schedule.next_episode"] = "next episode",
        ["schedule.episode_format"] = "Ep {0}",
        ["schedule.latest_ep"] = "Latest episode",
        ["schedule.premiere"] = "Premiere",
        ["schedule.premiere_lower"] = "premiere",
        ["schedule.releases_this_week"] = "{0} {1} this week",
        ["schedule.releases_past_days"] = "{0} {1} in recent days",
        ["schedule.release_1"] = "release",
        ["schedule.release_2_4"] = "releases",
        ["schedule.release_5_0"] = "releases",
        ["schedule.no_dates"] = "No dates",
        ["schedule.after_sync"] = "after sync",
        ["schedule.no_future_dates"] = "No future dates in current data",
        ["schedule.no_past_releases"] = "No recent releases",
        ["schedule.no_past_releases_desc"] = "No new episodes from your list were released recently",
        ["schedule.no_upcoming_releases"] = "No upcoming releases",
        ["schedule.after_sync_roadmap"] = "After synchronization, the episode roadmap will appear here"
    };

    public static string GetLoc(string key)
    {
        var val = LocalizationStore.Translate(key);
        if ((string.IsNullOrWhiteSpace(val) || string.Equals(val, key, StringComparison.OrdinalIgnoreCase))
            && FallbackStrings.TryGetValue(key, out var fallback))
        {
            return fallback;
        }
        return val;
    }

    public static string GetNoDatesText() => GetLoc("schedule.no_dates");
    public static string GetAfterSyncText() => GetLoc("schedule.after_sync");
    public static string GetLatestEpText() => GetLoc("schedule.latest_ep");
    public static string GetNoReleasesHeroText(ReleaseMapFilter filter) =>
        filter == ReleaseMapFilter.Past ? GetLoc("schedule.no_past_releases") : GetLoc("schedule.no_future_dates");
    public static string GetNoReleasesTitle(ReleaseMapFilter filter) =>
        filter == ReleaseMapFilter.Past ? GetLoc("schedule.no_past_releases") : GetLoc("schedule.no_upcoming_releases");
    public static string GetNoReleasesSubtitle(ReleaseMapFilter filter) =>
        filter == ReleaseMapFilter.Past ? GetLoc("schedule.no_past_releases_desc") : GetLoc("schedule.after_sync_roadmap");

    private readonly IEnumerable<AnimeEntity> _animeItems;

    public ReleaseMapViewModel(IEnumerable<AnimeEntity> animeItems)
    {
        _animeItems = animeItems;
    }

    public IEnumerable<ReleaseMapItem> GetUpcomingReleases()
    {
        var now = DateTime.UtcNow;
        return _animeItems
            .Select(item => CreateUpcomingReleaseMapItem(item, now))
            .Where(item => item != null)
            .Select(item => item!)
            .OrderBy(item => item.ReleaseAt);
    }

    public IEnumerable<ReleaseMapItem> GetPastReleases(int pastDays = 7)
    {
        var now = DateTime.UtcNow;
        return _animeItems
            .Select(item => CreatePastReleaseMapItem(item, now, pastDays))
            .Where(item => item != null)
            .Select(item => item!)
            .OrderByDescending(item => item.ReleaseAt);
    }

    public IEnumerable<ReleaseMapDayGroup> GetUpcomingReleaseGroups(int maxItems = 24)
    {
        var releases = GetUpcomingReleases().Take(maxItems).ToList();
        return GroupReleasesByDay(releases, isPastMode: false);
    }

    public IEnumerable<ReleaseMapDayGroup> GetPastReleaseGroups(int maxItems = 24)
    {
        var releases = GetPastReleases().Take(maxItems).ToList();
        return GroupReleasesByDay(releases, isPastMode: true);
    }

    public IEnumerable<ReleaseMapDayGroup> GetReleaseGroups(ReleaseMapFilter filter, int maxItems = 24)
    {
        return filter == ReleaseMapFilter.Past
            ? GetPastReleaseGroups(maxItems)
            : GetUpcomingReleaseGroups(maxItems);
    }

    private static IEnumerable<ReleaseMapDayGroup> GroupReleasesByDay(List<ReleaseMapItem> releases, bool isPastMode)
    {
        var groups = new List<ReleaseMapDayGroup>();
        var culture = GetReleaseCulture();
        var today = DateTime.Today;

        var grouped = releases.GroupBy(r => r.ReleaseAt.ToLocalTime().Date);
        if (isPastMode)
            grouped = grouped.OrderByDescending(g => g.Key);
        else
            grouped = grouped.OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var date = group.Key;
            string label = date == today ? GetLoc("schedule.today")
                : date == today.AddDays(1) ? GetLoc("schedule.tomorrow")
                : date == today.AddDays(-1) ? GetLoc("schedule.yesterday")
                : date == today.AddDays(-2) ? GetLoc("schedule.day_before_yesterday")
                : date.ToString("d MMMM, dddd", culture);

            var items = isPastMode
                ? group.OrderByDescending(r => r.ReleaseAt).ToList()
                : group.OrderBy(r => r.ReleaseAt).ToList();

            groups.Add(new ReleaseMapDayGroup(date, label.ToUpper(culture), items));
        }

        return groups;
    }

    private static ReleaseMapItem? CreateUpcomingReleaseMapItem(AnimeEntity item, DateTime now)
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
                nextEpisode > 0 ? string.Format(GetLoc("schedule.episode_format"), nextEpisode) : GetLoc("schedule.next_episode"),
                item.Presentation.AiringBadgeText,
                item.MainPictureUrl);
        }

        if (item.AiringDate.HasValue && item.AiringDate.Value >= now.Date)
        {
            return new ReleaseMapItem(
                GetPrimaryReleaseTitle(item),
                item,
                item.AiringDate.Value,
                GetLoc("schedule.premiere_lower"),
                item.Season,
                item.MainPictureUrl);
        }

        return null;
    }

    private static ReleaseMapItem? CreatePastReleaseMapItem(AnimeEntity item, DateTime now, int pastDays = 7)
    {
        if (item.Status == UserAnimeStatus.Dropped)
            return null;

        var minThreshold = now.AddDays(-pastDays);
        var recentCutoff = now.AddMinutes(-10);

        // Case 1: Anime with upcoming NextEpisodeAt in the next week.
        // For weekly airing series, the previous episode aired NextEpisodeAt - 7 days.
        if (item.NextEpisodeAt.HasValue && item.NextEpisodeAt.Value >= now && item.EpisodesAired > 0)
        {
            var prevAirAt = item.NextEpisodeAt.Value.AddDays(-7);
            if (prevAirAt >= minThreshold && prevAirAt <= now)
            {
                return new ReleaseMapItem(
                    GetPrimaryReleaseTitle(item),
                    item,
                    prevAirAt,
                    string.Format(GetLoc("schedule.episode_format"), item.EpisodesAired),
                    item.Presentation.AiringBadgeText,
                    item.MainPictureUrl);
            }
        }

        // Case 2: Anime whose NextEpisodeAt is in the past (e.g. yesterday/earlier this week, not yet synced)
        if (item.NextEpisodeAt.HasValue && item.NextEpisodeAt.Value < recentCutoff && item.NextEpisodeAt.Value >= minThreshold)
        {
            var ep = item.EpisodesAired + 1;
            if (item.TotalEpisodes > 0)
                ep = Math.Min(ep, item.TotalEpisodes);

            return new ReleaseMapItem(
                GetPrimaryReleaseTitle(item),
                item,
                item.NextEpisodeAt.Value,
                ep > 0 ? string.Format(GetLoc("schedule.episode_format"), ep) : GetLoc("schedule.latest_ep"),
                item.Presentation.AiringBadgeText,
                item.MainPictureUrl);
        }

        // Case 3: Anime recently completed or updated where LastEpisodeAt is within the past week
        if (item.LastEpisodeAt.HasValue && item.LastEpisodeAt.Value >= minThreshold && item.LastEpisodeAt.Value <= recentCutoff && item.EpisodesAired > 0)
        {
            return new ReleaseMapItem(
                GetPrimaryReleaseTitle(item),
                item,
                item.LastEpisodeAt.Value,
                string.Format(GetLoc("schedule.episode_format"), item.EpisodesAired),
                item.Presentation.AiringBadgeText,
                item.MainPictureUrl);
        }

        // Case 4: Premiere that aired in the past week
        if (item.AiringDate.HasValue && item.AiringDate.Value >= minThreshold.Date && item.AiringDate.Value <= now.Date && item.EpisodesAired <= 1)
        {
            return new ReleaseMapItem(
                GetPrimaryReleaseTitle(item),
                item,
                item.AiringDate.Value,
                GetLoc("schedule.premiere_lower"),
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
        if (date == today) return GetLoc("schedule.today").ToLower();
        if (date == today.AddDays(1)) return GetLoc("schedule.tomorrow").ToLower();
        if (date == today.AddDays(-1)) return GetLoc("schedule.yesterday").ToLower();
        if (date == today.AddDays(-2)) return GetLoc("schedule.day_before_yesterday").ToLower();

        var diff = (date - today).Days;
        if (diff > 1) return string.Format(GetLoc("schedule.days_later"), diff);
        if (diff < -2) return string.Format(GetLoc("schedule.days_ago"), Math.Abs(diff));

        return releaseAt.ToLocalTime().ToString("dd MMM", CultureInfo.CurrentCulture);
    }

    public static string FormatBadgeDate(DateTime releaseAt)
    {
        var today = DateTime.Today;
        var date = releaseAt.ToLocalTime().Date;
        if (date == today)
            return GetLoc("schedule.today");
        if (date == today.AddDays(1))
            return GetLoc("schedule.tomorrow");
        if (date == today.AddDays(-1))
            return GetLoc("schedule.yesterday");
        if (date == today.AddDays(-2))
            return GetLoc("schedule.day_before_yesterday");

        var diff = (date - today).Days;
        if (diff > 0)
            return string.Format(GetLoc("schedule.days_abbrev"), diff);
        if (diff < 0)
            return string.Format(GetLoc("schedule.days_ago"), Math.Abs(diff));

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
        return release.Kind.Contains(GetLoc("schedule.premiere_lower"), StringComparison.OrdinalIgnoreCase)
            ? GetLoc("schedule.premiere")
            : release.Kind;
    }

    public static string FormatWeekReleaseSummary(IReadOnlyCollection<ReleaseMapItem> releases)
    {
        var end = DateTime.UtcNow.AddDays(7);
        var count = releases.Count(release => release.ReleaseAt < end);
        return string.Format(GetLoc("schedule.releases_this_week"), count, PluralizeRelease(count));
    }

    public static string FormatPastReleaseSummary(IReadOnlyCollection<ReleaseMapItem> releases)
    {
        var count = releases.Count;
        return string.Format(GetLoc("schedule.releases_past_days"), count, PluralizeRelease(count));
    }

    public static string PluralizeRelease(int count)
    {
        var culture = GetReleaseCulture();
        if (culture.TwoLetterISOLanguageName != "ru")
            return count == 1 ? GetLoc("schedule.release_1") : GetLoc("schedule.release_5_0");

        var lastTwo = count % 100;
        if (lastTwo is >= 11 and <= 14)
            return GetLoc("schedule.release_5_0");

        return (count % 10) switch
        {
            1 => GetLoc("schedule.release_1"),
            >= 2 and <= 4 => GetLoc("schedule.release_2_4"),
            _ => GetLoc("schedule.release_5_0")
        };
    }

    public static string FormatUntilRelease(DateTime releaseAt)
    {
        var diff = releaseAt - DateTime.UtcNow;
        if (Math.Abs(diff.TotalMinutes) <= 1)
            return GetLoc("schedule.now");

        if (diff.TotalMinutes < -1)
        {
            var past = DateTime.UtcNow - releaseAt;
            if (past.TotalDays >= 2)
            {
                return string.Format(GetLoc("schedule.days_ago"), (int)Math.Floor(past.TotalDays));
            }
            if (releaseAt.ToLocalTime().Date == DateTime.Today.AddDays(-1))
            {
                return GetLoc("schedule.yesterday").ToLower();
            }
            if (past.TotalHours >= 1)
            {
                return string.Format(GetLoc("schedule.hours_ago"), (int)Math.Floor(past.TotalHours));
            }
            return string.Format(GetLoc("schedule.minutes_ago"), Math.Max(1, past.Minutes));
        }

        if (diff.TotalDays >= 1)
        {
            var days = (int)Math.Floor(diff.TotalDays);
            var hours = diff.Hours;
            return hours > 0 ? string.Format(GetLoc("schedule.days_hours"), days, hours) : string.Format(GetLoc("schedule.days_only"), days);
        }

        if (diff.TotalHours >= 1)
            return string.Format(GetLoc("schedule.hours_minutes"), (int)Math.Floor(diff.TotalHours), diff.Minutes);

        return string.Format(GetLoc("schedule.minutes_only"), Math.Max(1, diff.Minutes));
    }
}

