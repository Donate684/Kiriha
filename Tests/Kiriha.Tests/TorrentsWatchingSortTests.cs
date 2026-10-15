using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.ViewModels.Torrents;
using Moq;

namespace Kiriha.Tests;

public class TorrentsWatchingSortTests
{
    [Fact]
    public void RefreshWatchingList_SortsTitlesWithNewEpisodesToTopByCountDescendingThenTitle()
    {
        var animeList = new System.Collections.ObjectModel.ObservableCollection<AnimeEntity>
        {
            new AnimeEntity
            {
                Id = 1,
                Title = "Zeta Gundam",
                Status = UserAnimeStatus.Watching,
                MediaKind = MediaKind.Anime,
                Progress = 10,
                EpisodesAired = 10,
                TotalEpisodes = 50,
            },
            new AnimeEntity
            {
                Id = 2,
                Title = "Alpha Title",
                Status = UserAnimeStatus.Watching,
                MediaKind = MediaKind.Anime,
                Progress = 10,
                EpisodesAired = 10,
                TotalEpisodes = 12,
            },
            new AnimeEntity
            {
                Id = 3,
                Title = "Moderate Unseen",
                Status = UserAnimeStatus.Watching,
                MediaKind = MediaKind.Anime,
                Progress = 5,
                EpisodesAired = 10,
                TotalEpisodes = 24,
            },
            new AnimeEntity
            {
                Id = 4,
                Title = "Highest Unseen",
                Status = UserAnimeStatus.Watching,
                MediaKind = MediaKind.Anime,
                Progress = 0,
                EpisodesAired = 10,
                TotalEpisodes = 12,
            },
            new AnimeEntity
            {
                Id = 5,
                Title = "Single Unseen",
                Status = UserAnimeStatus.Watching,
                MediaKind = MediaKind.Anime,
                Progress = 9,
                EpisodesAired = 10,
                TotalEpisodes = 12,
            },
        };

        var animeRepoMock = new Mock<IAnimeRepository>();
        animeRepoMock.SetupGet(r => r.Collection).Returns(animeList);

        var filterRepoMock = new Mock<ITorrentFilterRepository>();
        filterRepoMock.Setup(r => r.GetHiddenAnimeIds()).Returns(new HashSet<int>());

        var rssMock = new Mock<Kiriha.Core.Abstractions.Services.IRssFeedService>();
        var settingsMock = new Mock<Kiriha.Core.Abstractions.Services.ISettingsService>();
        settingsMock.SetupGet(s => s.Current).Returns(new AppSettings());
        var torrServerMock = new Mock<Kiriha.Core.Abstractions.Services.ITorrServerService>();
        var localizerMock = new Mock<Kiriha.Core.Abstractions.Services.ILocalizer>();

        var vm = new TorrentsViewModel(rssMock.Object, animeRepoMock.Object, settingsMock.Object, filterRepoMock.Object, torrServerMock.Object, localizerMock.Object);

        var sortedIds = vm.HideMenuItems.Select(x => x.Anime.Id).ToList();

        // Expected order:
        // 1. Highest Unseen (Id 4, 10 unseen)
        // 2. Moderate Unseen (Id 3, 5 unseen)
        // 3. Single Unseen (Id 5, 1 unseen)
        // 4. Alpha Title (Id 2, 0 unseen, title A)
        // 5. Zeta Gundam (Id 1, 0 unseen, title Z)
        Assert.Equal([4, 3, 5, 2, 1], sortedIds);
    }

    [Fact]
    public void AvailableSortModes_DoNotContainLPrefix()
    {
        Assert.NotEmpty(TorrentsViewModel.AvailableSortModes);
        foreach (var mode in TorrentsViewModel.AvailableSortModes)
        {
            Assert.False(mode.LocalizationKey.StartsWith("l.", StringComparison.OrdinalIgnoreCase));
            Assert.StartsWith("torrents.sort.", mode.LocalizationKey, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("l.torrents.sort.newest", "torrents.sort.newest")]
    [InlineData("torrents.sort.newest", "torrents.sort.newest")]
    public void LocalizationStore_Translate_StripsLPrefixBeforeFallback(string inputKey, string expectedFallback)
    {
        // When Application.Current is null (in unit test headless runner), it returns key without "l."
        var result = Kiriha.Localization.LocalizationStore.Translate(inputKey);
        Assert.Equal(expectedFallback, result);
    }

    [Fact]
    public void TorrentsViewModel_OnlineFilters_InitializesFromSettings()
    {
        var settings = new AppSettings();
        settings.Torrents.OnlineStreaming.EnableAniLibria = false;
        settings.Torrents.OnlineStreaming.EnableAnimeLib = true;
        settings.Torrents.OnlineStreaming.EnableDreamCast = true;
        settings.Torrents.OnlineStreaming.EnableYummyAnime = false;

        var animeRepoMock = new Mock<IAnimeRepository>();
        animeRepoMock.SetupGet(r => r.Collection).Returns(new System.Collections.ObjectModel.ObservableCollection<AnimeEntity>());
        var filterRepoMock = new Mock<ITorrentFilterRepository>();
        filterRepoMock.Setup(r => r.GetHiddenAnimeIds()).Returns(new HashSet<int>());
        var rssMock = new Mock<Kiriha.Core.Abstractions.Services.IRssFeedService>();
        var settingsMock = new Mock<Kiriha.Core.Abstractions.Services.ISettingsService>();
        settingsMock.SetupGet(s => s.Current).Returns(settings);
        var torrServerMock = new Mock<Kiriha.Core.Abstractions.Services.ITorrServerService>();
        var localizerMock = new Mock<Kiriha.Core.Abstractions.Services.ILocalizer>();

        var vm = new TorrentsViewModel(rssMock.Object, animeRepoMock.Object, settingsMock.Object, filterRepoMock.Object, torrServerMock.Object, localizerMock.Object);

        Assert.False(vm.FilterOnlineAniLibria);
        Assert.True(vm.FilterOnlineAnimeLib);
        Assert.True(vm.FilterOnlineDreamCast);
        Assert.False(vm.FilterOnlineYummyAnime);

        var enabled = vm.GetEnabledOnlineProviders();
        Assert.Equal(2, enabled.Count);
        Assert.Contains("AnimeLib", enabled);
        Assert.Contains("DreamCast", enabled);
        Assert.DoesNotContain("AniLibria", enabled);
        Assert.DoesNotContain("YummyAnime", enabled);
    }

    [Fact]
    public void TorrentsViewModel_OnlineFilters_Change_PersistsToSettings()
    {
        var settings = new AppSettings();
        var animeRepoMock = new Mock<IAnimeRepository>();
        animeRepoMock.SetupGet(r => r.Collection).Returns(new System.Collections.ObjectModel.ObservableCollection<AnimeEntity>());
        var filterRepoMock = new Mock<ITorrentFilterRepository>();
        filterRepoMock.Setup(r => r.GetHiddenAnimeIds()).Returns(new HashSet<int>());
        var rssMock = new Mock<Kiriha.Core.Abstractions.Services.IRssFeedService>();
        var settingsMock = new Mock<Kiriha.Core.Abstractions.Services.ISettingsService>();
        settingsMock.SetupGet(s => s.Current).Returns(settings);
        settingsMock.Setup(s => s.Update(It.IsAny<Action<AppSettings>>(), It.IsAny<Kiriha.Core.Abstractions.Services.SettingsSection>(), It.IsAny<bool>()))
            .Callback<Action<AppSettings>, Kiriha.Core.Abstractions.Services.SettingsSection, bool>((action, _, _) => action(settings));

        var torrServerMock = new Mock<Kiriha.Core.Abstractions.Services.ITorrServerService>();
        var localizerMock = new Mock<Kiriha.Core.Abstractions.Services.ILocalizer>();

        var vm = new TorrentsViewModel(rssMock.Object, animeRepoMock.Object, settingsMock.Object, filterRepoMock.Object, torrServerMock.Object, localizerMock.Object);

        vm.FilterOnlineAniLibria = false;
        vm.FilterOnlineDreamCast = false;
        vm.FilterOnlineAnimeLib = true;

        Assert.False(settings.Torrents.OnlineStreaming.EnableAniLibria);
        Assert.False(settings.Torrents.OnlineStreaming.EnableDreamCast);
        Assert.True(settings.Torrents.OnlineStreaming.EnableAnimeLib);
    }
}
