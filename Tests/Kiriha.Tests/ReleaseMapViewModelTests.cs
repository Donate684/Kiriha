using System;
using System.Collections.Generic;
using System.Linq;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.ViewModels.AnimeList;
using Xunit;

namespace Kiriha.Tests;

public class ReleaseMapViewModelTests
{
    [Fact]
    public void GetUpcomingReleases_FiltersDroppedAndPastReleases()
    {
        var now = DateTime.UtcNow;
        var items = new List<AnimeEntity>
        {
            new AnimeEntity
            {
                Id = 1,
                Title = "Dropped Anime",
                Status = UserAnimeStatus.Dropped,
                NextEpisodeAt = now.AddDays(1)
            },
            new AnimeEntity
            {
                Id = 2,
                Title = "Future Anime",
                Status = UserAnimeStatus.Watching,
                NextEpisodeAt = now.AddDays(2),
                EpisodesAired = 5
            },
            new AnimeEntity
            {
                Id = 3,
                Title = "Past Anime",
                Status = UserAnimeStatus.Watching,
                NextEpisodeAt = now.AddDays(-2),
                EpisodesAired = 4
            }
        };

        var vm = new ReleaseMapViewModel(items);
        var upcoming = vm.GetUpcomingReleases().ToList();

        Assert.Single(upcoming);
        Assert.Equal("Future Anime", upcoming[0].Title);
        Assert.Equal(items[1].NextEpisodeAt, upcoming[0].ReleaseAt);
    }

    [Fact]
    public void GetPastReleases_IdentifiesWeeklyEpisodesAndUnsyncedSlots()
    {
        var now = DateTime.UtcNow;
        var items = new List<AnimeEntity>
        {
            // Anime 1: next episode in 6 days (meaning previous episode aired 1 day ago = yesterday)
            new AnimeEntity
            {
                Id = 1,
                Title = "Weekly Ongoing",
                Status = UserAnimeStatus.Watching,
                NextEpisodeAt = now.AddDays(6),
                EpisodesAired = 10,
                TotalEpisodes = 24
            },
            // Anime 2: next slot was yesterday (not yet synced)
            new AnimeEntity
            {
                Id = 2,
                Title = "Overdue Slot",
                Status = UserAnimeStatus.Watching,
                NextEpisodeAt = now.AddDays(-1),
                EpisodesAired = 3,
                TotalEpisodes = 12
            },
            // Anime 3: dropped anime with past date (should be excluded)
            new AnimeEntity
            {
                Id = 3,
                Title = "Dropped Past",
                Status = UserAnimeStatus.Dropped,
                NextEpisodeAt = now.AddDays(-1)
            }
        };

        var vm = new ReleaseMapViewModel(items);
        var past = vm.GetPastReleases(pastDays: 7).ToList();

        Assert.Equal(2, past.Count);
        Assert.Contains(past, r => r.Title == "Weekly Ongoing");
        Assert.Contains(past, r => r.Title == "Overdue Slot");

        var weeklyItem = past.First(r => r.Title == "Weekly Ongoing");
        // Episode aired 1 day ago (now.AddDays(6).AddDays(-7) = now.AddDays(-1))
        Assert.True(weeklyItem.ReleaseAt < now);
        Assert.True(weeklyItem.ReleaseAt >= now.AddDays(-2));
    }

    [Fact]
    public void GetReleaseGroups_RespectsFilterMode()
    {
        var now = DateTime.UtcNow;
        var items = new List<AnimeEntity>
        {
            new AnimeEntity
            {
                Id = 1,
                Title = "Yesterday Airing",
                Status = UserAnimeStatus.Watching,
                NextEpisodeAt = now.AddDays(-1),
                EpisodesAired = 1
            },
            new AnimeEntity
            {
                Id = 2,
                Title = "Tomorrow Premiere",
                Status = UserAnimeStatus.Watching,
                NextEpisodeAt = now.AddDays(1),
                EpisodesAired = 0 // Premiere: no past episodes
            }
        };

        var vm = new ReleaseMapViewModel(items);

        var pastGroups = vm.GetReleaseGroups(ReleaseMapFilter.Past).ToList();
        var upcomingGroups = vm.GetReleaseGroups(ReleaseMapFilter.Upcoming).ToList();

        Assert.Single(pastGroups);
        Assert.Equal("Yesterday Airing", pastGroups[0].Releases[0].Title);

        Assert.Single(upcomingGroups);
        Assert.Equal("Tomorrow Premiere", upcomingGroups[0].Releases[0].Title);
    }

    [Fact]
    public void FormatUntilRelease_HandlesPastAndFutureCorrectly()
    {
        var now = DateTime.UtcNow;

        // 3 days ago
        var threeDaysAgo = ReleaseMapViewModel.FormatUntilRelease(now.AddDays(-3));
        Assert.Contains("3", threeDaysAgo);

        // In 3 days
        var inThreeDays = ReleaseMapViewModel.FormatUntilRelease(now.AddDays(3).AddHours(2));
        Assert.Contains("3", inThreeDays);
    }

    [Fact]
    public void FormatBadgeDate_HandlesYesterdayAndPastProperly()
    {
        var now = DateTime.UtcNow;

        var yesterdayBadge = ReleaseMapViewModel.FormatBadgeDate(now.AddDays(-1));
        var twoDaysAgoBadge = ReleaseMapViewModel.FormatBadgeDate(now.AddDays(-2));

        Assert.NotNull(yesterdayBadge);
        Assert.NotNull(twoDaysAgoBadge);
    }
}
