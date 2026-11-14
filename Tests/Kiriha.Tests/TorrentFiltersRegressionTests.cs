using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Repository;
using Kiriha.ViewModels.Torrents;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public class TorrentFiltersRegressionTests
{
    private sealed class InMemoryFilterRepo : ITorrentFilterRepository
    {
        public Dictionary<int, AppSettings.TorrentFilterSet> Saved { get; } = new();
        public HashSet<int> Hidden { get; } = new();

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public IReadOnlySet<int> GetHiddenAnimeIds() => new HashSet<int>(Hidden);

        public bool IsAnimeHidden(int animeId) => Hidden.Contains(animeId);

        public Task SetAnimeHiddenAsync(int animeId, bool hidden, CancellationToken ct = default)
        {
            if (hidden) Hidden.Add(animeId);
            else Hidden.Remove(animeId);
            return Task.CompletedTask;
        }

        public AppSettings.TorrentFilterSet? TryGetCachedFilter(int animeId) =>
            Saved.TryGetValue(animeId, out var s) ? Clone(s) : null;

        public Task<AppSettings.TorrentFilterSet?> GetFilterAsync(int animeId, CancellationToken ct = default) =>
            Task.FromResult(TryGetCachedFilter(animeId));

        public Task SaveFilterAsync(int animeId, AppSettings.TorrentFilterSet filter, CancellationToken ct = default)
        {
            Saved[animeId] = Clone(filter);
            return Task.CompletedTask;
        }

        private static AppSettings.TorrentFilterSet Clone(AppSettings.TorrentFilterSet s) => new()
        {
            OnlyCrunchyroll = s.OnlyCrunchyroll,
            FilterNetflix = s.FilterNetflix,
            FilterAmazon = s.FilterAmazon,
            FilterHidive = s.FilterHidive,
            FilterVaryg = s.FilterVaryg,
            FilterEraiRaws = s.FilterEraiRaws,
            FilterToonsHub = s.FilterToonsHub,
            FilterJudas = s.FilterJudas,
            FilterHevc = s.FilterHevc,
            Filter1080p = s.Filter1080p,
            FilterOnlyNew = s.FilterOnlyNew,
            FilterOnlyBatches = s.FilterOnlyBatches,
            UseCustomQuery = s.UseCustomQuery,
            CustomQuery = s.CustomQuery
        };
    }

    private static (TorrentsViewModel Vm, AppSettings Settings, InMemoryFilterRepo FilterRepo, Mock<IRssFeedService> RssMock) CreateTestHarness(
        AppSettings? initialSettings = null,
        IEnumerable<AnimeEntity>? animeItems = null,
        InMemoryFilterRepo? filterRepo = null)
    {
        var settings = initialSettings ?? new AppSettings();
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.SetupGet(s => s.Current).Returns(settings);
        settingsMock
            .Setup(s => s.Update(It.IsAny<Action<AppSettings>>(), It.IsAny<SettingsSection>(), It.IsAny<bool>()))
            .Callback<Action<AppSettings>, SettingsSection, bool>((act, _, _) => act(settings));

        var collection = new ObservableCollection<AnimeEntity>(animeItems ?? []);
        var animeRepoMock = new Mock<IAnimeRepository>();
        animeRepoMock.SetupGet(r => r.Collection).Returns(collection);

        var repo = filterRepo ?? new InMemoryFilterRepo();
        var rssMock = new Mock<IRssFeedService>();
        rssMock
            .Setup(r => r.SearchTorrentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var torrServerMock = new Mock<ITorrServerService>();
        var localizerMock = new Mock<ILocalizer>();
        localizerMock.Setup(l => l.GetLoc(It.IsAny<string>())).Returns((string k) => k);
        localizerMock.Setup(l => l.GetLoc(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string k, object[] _) => k);

        var vm = new TorrentsViewModel(
            rssMock.Object,
            animeRepoMock.Object,
            settingsMock.Object,
            repo,
            torrServerMock.Object,
            localizerMock.Object);

        return (vm, settings, repo, rssMock);
    }

    [Fact]
    public void Startup_LegacyFiltersPerTitleFalse_SelfHealsAndLoadsPerTitleFiltersFromRepo()
    {
        var settings = new AppSettings();
        settings.Torrents.FiltersPerTitle = false;
        settings.Torrents.UseCustomQuery = true;
        settings.Torrents.CustomQuery = "Re:Zero kara Hajimeru Isekai Seikatsu 4th";

        var animeLaidOff = new AnimeEntity { Id = 63431, Title = "Kaiko sareta Ankoku Heishi", Status = UserAnimeStatus.Watching };
        var animeGhost = new AnimeEntity { Id = 64718, Title = "Ghost Meets Gal!", Status = UserAnimeStatus.Watching };

        var filterRepo = new InMemoryFilterRepo();
        filterRepo.Saved[63431] = new AppSettings.TorrentFilterSet
        {
            FilterVaryg = true,
            UseCustomQuery = true,
            CustomQuery = "The Laid Off"
        };
        filterRepo.Saved[64718] = new AppSettings.TorrentFilterSet
        {
            FilterToonsHub = true,
            UseCustomQuery = false,
            CustomQuery = "Re:Zero kara Hajimeru Isekai Seikatsu 4th" // stale DB string alongside UseCustomQuery=false
        };

        var (vm, _, _, _) = CreateTestHarness(settings, [animeLaidOff, animeGhost], filterRepo);

        // Legacy poisoned global config must be healed automatically on startup
        Assert.True(settings.Torrents.FiltersPerTitle);
        Assert.False(settings.Torrents.UseCustomQuery);
        Assert.Null(settings.Torrents.CustomQuery);
        Assert.False(vm.UseCustomQuery);

        // Selecting anime 63431 loads its own saved custom formula + VARYG filter from repo
        vm.SelectAnime(animeLaidOff);
        Assert.True(vm.FilterVaryg);
        Assert.True(vm.UseCustomQuery);
        Assert.Equal("The Laid Off", vm.CustomQuery);
        Assert.Equal("The Laid Off", vm.SearchQuery);
        Assert.Equal("The Laid Off VARYG", vm.PreviewQuery);

        // Selecting anime 64718 loads ToonsHub and ignores stale CustomQuery when UseCustomQuery=false
        vm.SelectAnime(animeGhost);
        Assert.False(vm.FilterVaryg);
        Assert.True(vm.FilterToonsHub);
        Assert.False(vm.UseCustomQuery);
        Assert.Equal("Ghost Meets Gal!", vm.CustomQuery);
        Assert.Equal("Ghost Meets Gal!", vm.SearchQuery);
        Assert.Equal("Ghost Meets Gal! ToonsHub", vm.PreviewQuery);
    }

    [Fact]
    public void GlobalCustomFormula_DoesNotLeakToSelectedAnimeWithoutPerTitleRecord()
    {
        var settings = new AppSettings();
        settings.Torrents.FiltersPerTitle = true;
        settings.Torrents.Filter1080p = true;
        settings.Torrents.UseCustomQuery = true;
        settings.Torrents.CustomQuery = "Global Custom Formula";

        var anime = new AnimeEntity { Id = 52991, Title = "Sousou no Frieren", Status = UserAnimeStatus.Watching };
        var (vm, _, _, _) = CreateTestHarness(settings, [anime]);

        // Global context (titleId == 0) still honors global formula
        Assert.True(vm.UseCustomQuery);
        Assert.Equal("Global Custom Formula", vm.CustomQuery);

        // Selecting a specific anime inherits chip defaults (1080p) but never inherits global custom formula
        vm.SelectAnime(anime);
        Assert.True(vm.Filter1080p);
        Assert.False(vm.UseCustomQuery);
        Assert.Equal("Sousou no Frieren", vm.CustomQuery);
        Assert.Equal("Sousou no Frieren", vm.SearchQuery);
    }

    [Fact]
    public void ClearingCustomQueryTextbox_DoesNotDeselectAnimeOrPolluteGlobalSettings()
    {
        var anime = new AnimeEntity
        {
            Id = 61316,
            Title = "Re:Zero kara Hajimeru Isekai Seikatsu 4th Season",
            Status = UserAnimeStatus.Watching
        };

        var (vm, settings, filterRepo, _) = CreateTestHarness(animeItems: [anime]);

        vm.SelectAnime(anime);
        vm.UseCustomQuery = true;
        vm.CustomQuery = "Re:Zero 4th";

        // Simulate user clearing the CustomQuery textbox (Ctrl+A + Backspace) before typing a new formula
        vm.CustomQuery = string.Empty;

        Assert.Same(anime, vm.SelectedAnime);
        Assert.True(vm.HasSelectedAnime);
        Assert.False(settings.Torrents.UseCustomQuery);
        Assert.Null(settings.Torrents.CustomQuery);

        vm.CustomQuery = "Re Zero S4";

        Assert.Same(anime, vm.SelectedAnime);
        Assert.False(settings.Torrents.UseCustomQuery);
        Assert.Null(settings.Torrents.CustomQuery);
        Assert.True(filterRepo.Saved.TryGetValue(61316, out var saved));
        Assert.True(saved.UseCustomQuery);
        Assert.Equal("Re Zero S4", saved.CustomQuery);
    }

    [Fact]
    public void HasActiveFilters_And_ClearFilters_WorkWhenOnlyCustomQueryIsActive()
    {
        var anime = new AnimeEntity
        {
            Id = 61316,
            Title = "Re:Zero kara Hajimeru Isekai Seikatsu 4th Season",
            Status = UserAnimeStatus.Watching
        };

        var filterRepo = new InMemoryFilterRepo();
        filterRepo.Saved[61316] = new AppSettings.TorrentFilterSet
        {
            UseCustomQuery = true,
            CustomQuery = "Re:Zero kara Hajimeru Isekai Seikatsu 4th"
        };

        var (vm, _, _, _) = CreateTestHarness(animeItems: [anime], filterRepo: filterRepo);

        vm.SelectAnime(anime);
        Assert.True(vm.UseCustomQuery);
        Assert.True(vm.HasActiveFilters);

        vm.ClearFilters();

        Assert.False(vm.UseCustomQuery);
        Assert.False(vm.HasActiveFilters);
        Assert.Equal(anime.Title, vm.SearchQuery);
        Assert.Equal(anime.Title, vm.PreviewQuery);
        Assert.True(filterRepo.Saved.TryGetValue(61316, out var cleared));
        Assert.False(cleared.UseCustomQuery);
        Assert.Null(cleared.CustomQuery);
    }

    [Fact]
    public void TabSwitch_SyncFilterContextAndDetachedComboBoxNull_PreserveFilterAndSortState()
    {
        var anime = new AnimeEntity { Id = 64718, Title = "Ghost Meets Gal!", Status = UserAnimeStatus.Watching };
        var (vm, _, filterRepo, _) = CreateTestHarness(animeItems: [anime]);

        vm.SelectAnime(anime);
        vm.Filter1080p = true;
        vm.UseCustomQuery = true;
        vm.CustomQuery = "Ghost Gal";
        vm.SortMode = TorrentSortMode.ReleaseGroup;

        // Simulate tab switch detach/re-attach (ComboBox SelectedItem binding pushing null + NavigateTorrents + Flyout open)
        vm.SelectedSortOption = null;
        vm.RefreshWatchingList();
        vm.SyncFilterContext();

        Assert.Same(anime, vm.SelectedAnime);
        Assert.True(vm.Filter1080p);
        Assert.True(vm.UseCustomQuery);
        Assert.Equal("Ghost Gal", vm.CustomQuery);
        Assert.Equal("Ghost Gal", vm.SearchQuery);
        Assert.Equal(TorrentSortMode.ReleaseGroup, vm.SortMode);
        Assert.NotNull(vm.SelectedSortOption);
        Assert.Equal(TorrentSortMode.ReleaseGroup, vm.SelectedSortOption!.Mode);
        Assert.True(filterRepo.Saved[64718].Filter1080p);
        Assert.True(filterRepo.Saved[64718].UseCustomQuery);
        Assert.Equal("Ghost Gal", filterRepo.Saved[64718].CustomQuery);
    }

    [Fact]
    public void OnlineModeSwitch_PreservesPerTitleCustomTorrentFormulaWithoutAffectingOnlineQuery()
    {
        var anime = new AnimeEntity
        {
            Id = 52991,
            Title = "Sousou no Frieren",
            RussianTitle = "Провожающая в последний путь Фрирен",
            Status = UserAnimeStatus.Watching
        };

        var filterRepo = new InMemoryFilterRepo();
        filterRepo.Saved[52991] = new AppSettings.TorrentFilterSet
        {
            UseCustomQuery = true,
            CustomQuery = "Frieren Beyond Journeys End"
        };

        var (vm, _, _, _) = CreateTestHarness(animeItems: [anime], filterRepo: filterRepo);

        vm.SelectAnime(anime);
        Assert.Equal("Frieren Beyond Journeys End", vm.SearchQuery);

        vm.IsOnlineMode = true;
        Assert.Equal("Провожающая в последний путь Фрирен", vm.SearchQuery);

        vm.IsOnlineMode = false;
        Assert.Equal("Frieren Beyond Journeys End", vm.SearchQuery);
    }

    [Fact]
    public async Task TorrentFilterRepository_SqliteRoundTrip_PersistsAndUpdatesUnderNoTracking()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"kiriha_filter_repo_test_{Guid.NewGuid()}.db");
        try
        {
            var opts = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={tempDb}")
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .AddInterceptors(new SqlitePragmaConnectionInterceptor())
                .Options;

            await using (var initCtx = new AppDbContext(opts))
            {
                await initCtx.Database.EnsureCreatedAsync();
            }

            var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
            factoryMock.Setup(f => f.CreateDbContext()).Returns(() => new AppDbContext(opts));
            factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new AppDbContext(opts));

            var repo1 = new TorrentFilterRepository(factoryMock.Object);
            await repo1.InitializeAsync();

            // Insert new per-title filter
            await repo1.SaveFilterAsync(64718, new AppSettings.TorrentFilterSet
            {
                FilterToonsHub = true,
                Filter1080p = true,
                UseCustomQuery = true,
                CustomQuery = "Ghost Gal"
            });

            // Update existing per-title filter (disable custom query, add HEVC)
            await repo1.SaveFilterAsync(64718, new AppSettings.TorrentFilterSet
            {
                FilterToonsHub = true,
                Filter1080p = true,
                FilterHevc = true,
                UseCustomQuery = false,
                CustomQuery = null
            });

            // Reload in a brand-new repository instance
            var repo2 = new TorrentFilterRepository(factoryMock.Object);
            await repo2.InitializeAsync();

            var loaded = repo2.TryGetCachedFilter(64718);
            Assert.NotNull(loaded);
            Assert.True(loaded!.FilterToonsHub);
            Assert.True(loaded.Filter1080p);
            Assert.True(loaded.FilterHevc);
            Assert.False(loaded.UseCustomQuery);
            Assert.Null(loaded.CustomQuery);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb)) File.Delete(tempDb);
        }
    }

    [Fact]
    public async Task PerformSearch_WithSelectedAnimeAndCustomQuery_MarksUnwatchedEpisodesAsMatched()
    {
        var anime = new AnimeEntity
        {
            Id = 63431,
            Title = "Kaiko sareta Ankoku Heishi",
            Progress = 3,
            Status = UserAnimeStatus.Watching
        };

        var filterRepo = new InMemoryFilterRepo();
        filterRepo.Saved[63431] = new AppSettings.TorrentFilterSet
        {
            FilterVaryg = true,
            UseCustomQuery = true,
            CustomQuery = "The Laid Off"
        };

        var (vm, _, _, rssMock) = CreateTestHarness(animeItems: [anime], filterRepo: filterRepo);

        rssMock
            .Setup(r => r.SearchTorrentsAsync("The Laid Off VARYG", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new TorrentEntity { Title = "[VARYG] The Laid Off - 03 (1080p)", AnimeTitle = "The Laid Off", Episode = "03", IsMatched = false },
                new TorrentEntity { Title = "[VARYG] The Laid Off - 04 (1080p)", AnimeTitle = "The Laid Off", Episode = "04", IsMatched = false }
            ]);

        vm.SelectAnime(anime);
        await vm.PerformSearch();

        var ep3 = vm.Torrents.Single(t => t.Episode == "03");
        var ep4 = vm.Torrents.Single(t => t.Episode == "04");
        Assert.False(ep3.IsMatched);
        Assert.True(ep4.IsMatched);
    }

    [Fact]
    public void RssFeedService_MatchActiveAnime_MatchesPunctuationAlternativeTitlesAndSequelSeasons()
    {
        var reZero = new AnimeEntity
        {
            Id = 10,
            Title = "Re:Zero kara Hajimeru Isekai Seikatsu",
            Status = UserAnimeStatus.Watching
        };
        var danMachi = new AnimeEntity
        {
            Id = 20,
            Title = "Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka",
            AlternativeTitles = ["DanMachi"],
            Status = UserAnimeStatus.Watching
        };
        var soloS1 = new AnimeEntity
        {
            Id = 30,
            Title = "Solo Leveling",
            Status = UserAnimeStatus.Watching
        };
        var soloS2 = new AnimeEntity
        {
            Id = 31,
            Title = "Solo Leveling Season 2: Arise from the Shadow",
            Status = UserAnimeStatus.Watching
        };

        List<AnimeEntity> active = [reZero, danMachi, soloS1, soloS2];

        // Punctuation-insensitive match ("Re Zero..." == "Re:Zero...")
        var matchedReZero = RssFeedService.MatchActiveAnime(active, "Re Zero kara Hajimeru Isekai Seikatsu", parsedSeason: null);
        Assert.Same(reZero, matchedReZero);

        // AlternativeTitles match
        var matchedAlt = RssFeedService.MatchActiveAnime(active, "DanMachi", parsedSeason: null);
        Assert.Same(danMachi, matchedAlt);

        // Sequel season 2 matches Season 2 entry, NOT Season 1
        var matchedS2 = RssFeedService.MatchActiveAnime(active, "Solo Leveling", parsedSeason: 2);
        Assert.Same(soloS2, matchedS2);

        // If user only has Season 1 active, Season 2 torrent does not falsely match Season 1
        var unmatchedS2 = RssFeedService.MatchActiveAnime([soloS1], "Solo Leveling", parsedSeason: 2);
        Assert.Null(unmatchedS2);
    }
}
