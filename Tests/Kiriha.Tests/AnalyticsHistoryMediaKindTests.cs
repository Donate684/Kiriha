using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.ViewModels.Analytics;
using Moq;

namespace Kiriha.Tests;

public sealed class AnalyticsHistoryMediaKindTests
{
    [Theory]
    [InlineData(MediaKind.Manga)]
    [InlineData(MediaKind.LightNovel)]
    public void ReleaseYearsAndCompletionMonths_IncludeOnlyAnime(MediaKind readingKind)
    {
        var vm = CreateViewModel();
        var anime = NewCompletedItem(MediaKind.Anime, 2020, new DateTime(2024, 3, 15));
        var reading = NewCompletedItem(readingKind, 2010, new DateTime(2023, 6, 20));

        // IDs may overlap between anime and manga in the shared library.
        vm.Refresh([], [reading, anime], [reading, anime]);

        var releaseYear = Assert.Single(vm.ReleaseYearCompletions);
        Assert.Equal("2020", releaseYear.Label);
        Assert.Equal(1, releaseYear.Count);
        var year = Assert.Single(vm.YearDistribution);
        Assert.Equal("2020", year.Label);
        Assert.Equal(1, year.Count);

        Assert.True(vm.HasMonthlyHistory);
        var cell = Assert.Single(vm.MonthlyHistory.SelectMany(row => row.Months), month => month.Count > 0);
        Assert.Equal(3, cell.Month);
        Assert.Equal(1, cell.Count);
        Assert.Equal(2024, Assert.Single(vm.MonthlyHistory, row => row.Months.Contains(cell)).Year);
        Assert.Equal(anime.Title, Assert.Single(cell.Entries).Title);

        vm.OpenMonthlyHistory(cell);
        Assert.Equal(anime.Title, Assert.Single(vm.HistoryPopupEntries).Title);
    }

    [Theory]
    [InlineData(MediaKind.Manga)]
    [InlineData(MediaKind.LightNovel)]
    public void RecentActivity_ExcludesChaptersAndUsesAnimePosterForOverlappingIds(MediaKind readingKind)
    {
        var vm = CreateViewModel();
        var anime = NewCompletedItem(MediaKind.Anime, 2020, DateTime.Today);
        var reading = NewCompletedItem(readingKind, 2010, DateTime.Today);
        var animeHistory = NewActivity(MediaKind.Anime);
        var readingHistory = NewActivity(readingKind);
        readingHistory.Episode = 50;

        vm.Refresh([readingHistory, animeHistory], [reading, anime], [reading, anime]);

        Assert.Equal(14, vm.RecentHistory.Count);
        Assert.Equal(1, vm.RecentHistoryEpisodes);
        Assert.Equal(1, vm.RecentHistoryTitles);
        var today = vm.RecentHistory[^1];
        Assert.Equal(1, today.Count);
        var entry = Assert.Single(today.Entries);
        Assert.Equal(animeHistory.AnimeTitle, entry.Title);
        Assert.Equal(anime.MainPictureUrl, entry.PosterUrl);

        vm.OpenDailyHistory(today);
        Assert.Equal(animeHistory.AnimeTitle, Assert.Single(vm.HistoryPopupEntries).Title);
    }

    [Theory]
    [InlineData(MediaKind.Manga)]
    [InlineData(MediaKind.LightNovel)]
    public void RefreshWithOnlyReadingItems_ClearsPreviousAnimeStatisticsAndPopup(MediaKind readingKind)
    {
        var vm = CreateViewModel();
        var anime = NewCompletedItem(MediaKind.Anime, 2020, DateTime.Today);
        vm.Refresh([NewActivity(MediaKind.Anime)], [anime], [anime]);
        vm.OpenDailyHistory(vm.RecentHistory[^1]);
        Assert.True(vm.IsHistoryPopupOpen);
        Assert.True(vm.HasMonthlyHistory);
        Assert.Equal(1, vm.RecentHistoryEpisodes);

        var reading = NewCompletedItem(readingKind, 2010, DateTime.Today);
        vm.Refresh([NewActivity(readingKind)], [reading], [reading]);

        Assert.Empty(vm.RecentHistory);
        Assert.Empty(vm.MonthlyHistory);
        Assert.Empty(vm.ReleaseYearCompletions);
        Assert.Empty(vm.YearDistribution);
        Assert.Equal(0, vm.RecentHistoryEpisodes);
        Assert.Equal(0, vm.RecentHistoryTitles);
        Assert.False(vm.HasMonthlyHistory);
        Assert.False(vm.IsHistoryPopupOpen);
        Assert.Empty(vm.HistoryPopupEntries);
    }

    [Fact]
    public void ReadingCompletions_DoNotCreateAnimeHistoryWhenAnimeIsStillWatching()
    {
        var vm = CreateViewModel();
        var anime = NewCompletedItem(MediaKind.Anime, 2020, DateTime.Today);
        anime.Status = UserAnimeStatus.Watching;
        anime.DateCompleted = null;
        var manga = NewCompletedItem(MediaKind.Manga, 2010, DateTime.Today);

        vm.Refresh([NewActivity(MediaKind.Manga)], [anime, manga], [manga]);

        Assert.False(vm.HasMonthlyHistory);
        Assert.Empty(vm.MonthlyHistory);
        Assert.Empty(vm.ReleaseYearCompletions);
        Assert.Empty(vm.YearDistribution);
        Assert.Equal(0, vm.RecentHistoryEpisodes);
        Assert.Equal(0, vm.RecentHistoryTitles);
        Assert.All(vm.RecentHistory, point => Assert.Equal(0, point.Count));
    }

    private static HistorySectionViewModel CreateViewModel()
    {
        var localizer = new Mock<ILocalizer>();
        localizer.Setup(x => x.GetLoc(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns<string, object[]>((key, _) => key);
        return new HistorySectionViewModel(localizer.Object);
    }

    private static AnimeEntity NewCompletedItem(MediaKind kind, int releaseYear, DateTime completed) => new()
    {
        Id = 42,
        MediaKind = kind,
        Title = $"{kind} title",
        MainPictureUrl = $"https://example.com/{kind}.jpg",
        Status = UserAnimeStatus.Completed,
        StartYear = releaseYear,
        DateCompleted = completed
    };

    private static HistoryItem NewActivity(MediaKind kind) => new()
    {
        AnimeId = 42,
        MediaKind = kind,
        AnimeTitle = $"{kind} title",
        ActionType = (int)HistoryActionType.Watched,
        Episode = 7,
        Timestamp = DateTime.Now.ToUniversalTime()
    };
}
