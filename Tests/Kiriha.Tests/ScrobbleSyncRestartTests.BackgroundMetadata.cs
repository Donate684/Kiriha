using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Streaming.Models;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Metadata;
using Kiriha.ViewModels.NowPlaying;
using Kiriha.ViewModels.Torrents;
using Moq;

namespace Kiriha.Tests;

public sealed partial class ScrobbleSyncRestartTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AiringRefresh_AfterScrobbleOrRemoval_OnlyUpdatesOriginalEntry(
        bool remove, bool reAdd)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var entry = new AnimeEntity
        {
            Id = AnimeId, Title = "Test title", EpisodesAired = 3, Notes = "My note", StatusDetailed = "currently_airing"
        };
        Assert.True((await scenario.Actions.AddToListAsync(entry, UserAnimeStatus.Watching, 2, ct)).Success);

        var response = new TaskCompletionSource<EpisodeAiringInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, notifications) = CreateAiringService(scenario, _ => response.Task);
        var refresh = service.SyncOngoingEpisodesAsync(force: true, ct: ct);
        Assert.False(refresh.IsCompleted);
        Assert.True(await scenario.Progress.RecordScrobbleAsync(entry, 4, null, ct));
        if (remove)
            Assert.True((await scenario.Actions.RemoveFromListAsync(AnimeId, ct)).Success);
        if (reAdd)
        {
            // Reuse the object so reference equality cannot distinguish the replacement.
            Assert.True((await scenario.Actions.AddToListAsync(entry, UserAnimeStatus.Watching, 1, ct)).Success);
        }

        var nextSlot = DateTime.UtcNow.AddDays(3);
        response.SetResult(new EpisodeAiringInfo(9, AnimeId, "RELEASING", 6, nextSlot, 12));
        await refresh.WaitAsync(ct);

        var saved = await scenario.Store.GetByIdAsync(AnimeId, ct: ct);
        if (remove && !reAdd)
        {
            Assert.Null(saved);
            Assert.Null(scenario.Library.FindById(AnimeId));
        }
        else
        {
            Assert.NotNull(saved);
            Assert.Equal(reAdd ? 1 : 4, saved.Progress);
            Assert.Equal(reAdd ? 3 : 5, saved.EpisodesAired);
            Assert.Equal(reAdd ? null : nextSlot, saved.NextEpisodeAt);
            Assert.Equal("My note", saved.Notes);
            Assert.Equal(saved.EpisodesAired, entry.EpisodesAired);
        }

        notifications.Verify(n => n.NotifyNewEpisode(entry, 5), remove ? Times.Never() : Times.Once());
        var expectedActions = 2 + (remove ? 1 : 0) + (reAdd ? 1 : 0);
        Assert.Equal(expectedActions, (await scenario.History.GetHistoryAsync(ct: ct)).Count);
        Assert.Equal(expectedActions, (await scenario.Queue.GetPendingAsync(ct)).Count);
    }

    [Fact]
    public async Task AiringRefresh_WhenOlderRequestFinishesLast_KeepsNewestSchedule()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var entry = new AnimeEntity { Id = AnimeId, Title = "Test title", EpisodesAired = 3, TotalEpisodes = 12 };
        Assert.True((await scenario.Actions.AddToListAsync(entry, UserAnimeStatus.Watching, 1, ct)).Success);
        var oldResponse = new TaskCompletionSource<EpisodeAiringInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextSlot = DateTime.UtcNow.AddDays(3);
        int count = 0;
        var (service, notifications) = CreateAiringService(scenario, _ => Interlocked.Increment(ref count) == 1
            ? oldResponse.Task
            : Task.FromResult<EpisodeAiringInfo?>(new(9, AnimeId, "RELEASING", 7, nextSlot, 12)));

        var oldRefresh = service.SyncEpisodesForAnimeAsync(entry, ct);
        await service.SyncEpisodesForAnimeAsync(entry, ct);
        oldResponse.SetResult(new EpisodeAiringInfo(9, AnimeId, "RELEASING", 5, nextSlot.AddDays(-1), 12));
        await oldRefresh.WaitAsync(ct);

        var saved = await scenario.Store.GetByIdAsync(AnimeId, ct: ct);
        Assert.NotNull(saved);
        Assert.Equal((6, nextSlot), (saved.EpisodesAired, saved.NextEpisodeAt));
        notifications.Verify(n => n.NotifyNewEpisode(entry, It.IsAny<int>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NowPlaying_WhenDetailsArriveLate_PreservesProgressAndMembership(bool replace)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var entry = new AnimeEntity { Id = AnimeId, Title = "Test title", Notes = "My note" };
        Assert.True((await scenario.Actions.AddToListAsync(entry, UserAnimeStatus.Watching, 2, ct)).Success);
        var detachedMatch = entry.Clone();

        var response = new TaskCompletionSource<AnimeEntity?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new Mock<IMalApiService>();
        api.Setup(a => a.GetAnimeDetailsAsync(AnimeId, It.IsAny<CancellationToken>())).Returns(response.Task);
        var tracking = new Mock<ITrackingService>();
        using var vm = new NowPlayingViewModel(tracking.Object, CreateMetadataSettings(), scenario.Library,
            scenario.Actions, Mock.Of<IShikiMetadataService>(), api.Object, ILocalizer.Passthrough);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(NowPlayingViewModel.IsLoadingDetails) && !vm.IsLoadingDetails)
                finished.TrySetResult();
        };
        vm.MatchedAnime = detachedMatch;
        Assert.True(vm.IsLoadingDetails);

        Assert.True(await scenario.Progress.RecordScrobbleAsync(entry, 4, null, ct));
        if (replace)
        {
            Assert.True((await scenario.Actions.RemoveFromListAsync(AnimeId, ct)).Success);
            Assert.True((await scenario.Actions.AddToListAsync(entry, UserAnimeStatus.Watching, 1, ct)).Success);
        }

        response.SetResult(new AnimeEntity
        {
            Id = AnimeId, Title = entry.Title, Status = UserAnimeStatus.Completed, Progress = 12,
            TotalEpisodes = 12, Notes = "Old remote note", Synopsis = "Full synopsis", Genres = ["Drama"]
        });
        await finished.Task.WaitAsync(ct);

        var saved = await scenario.Store.GetByIdAsync(AnimeId, ct: ct);
        Assert.NotNull(saved);
        Assert.Equal((UserAnimeStatus.Watching, replace ? 1 : 4, "My note"), (saved.Status, saved.Progress, saved.Notes));
        Assert.Equal(replace ? entry.Synopsis : "Full synopsis", saved.Synopsis);
        Assert.Equal(replace ? 0 : 12, detachedMatch.TotalEpisodes);
        tracking.Verify(t => t.NotifyCurrentMediaMetadataUpdated(entry), replace ? Times.Never() : Times.Once());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShikiLocalization_WhenCacheReadFinishesLate_UpdatesOnlyCurrentMembership(bool replace)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var entry = new AnimeEntity { Id = AnimeId, Title = "Test title", TotalEpisodes = 12 };
        Assert.True((await scenario.Actions.AddToListAsync(entry, UserAnimeStatus.Watching, 2, ct)).Success);
        var detachedMatch = entry.Clone();
        var response = new TaskCompletionSource<ShikiMetadata?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = new Mock<IMetadataRepository>();
        metadata.Setup(m => m.GetAsync(AnimeId, MediaKind.Anime, It.IsAny<CancellationToken>())).Returns(response.Task);
        using var client = new HttpClient();
        using var limiter = new ShikiRateLimiter();
        var clientFactory = Mock.Of<IHttpClientFactory>(factory => factory.CreateClient(It.IsAny<string>()) == client);
        using var shiki = new ShikiMetadataService(clientFactory, CreateMetadataSettings(), metadata.Object,
            scenario.Store, Mock.Of<IHttpCacheRepository>(), new ShikiHostResolver(), new ImmediateProgressDispatcher(),
            limiter, library: scenario.Library, syncGate: scenario.Gate);

        var localization = shiki.EnsureLocalizedAsync(detachedMatch, ct);
        Assert.False(localization.IsCompleted);
        Assert.True(await scenario.Progress.RecordScrobbleAsync(entry, 4, null, ct));
        if (replace)
        {
            Assert.True((await scenario.Actions.RemoveFromListAsync(AnimeId, ct)).Success);
            Assert.True((await scenario.Actions.AddToListAsync(entry, UserAnimeStatus.Watching, 1, ct)).Success);
        }

        response.SetResult(new ShikiMetadata
        {
            Id = AnimeId, Russian = "Русское название", PosterUrl = "https://test.invalid/poster.png", FetchedAt = DateTime.UtcNow
        });
        await localization.WaitAsync(ct);

        var saved = await scenario.Store.GetByIdAsync(AnimeId, ct: ct);
        Assert.NotNull(saved);
        Assert.Equal(replace ? 1 : 4, saved.Progress);
        Assert.Equal(replace ? entry.RussianTitle : "Русское название", saved.RussianTitle);
        Assert.Equal(saved.RussianTitle, detachedMatch.RussianTitle);
    }

    [Fact]
    public async Task NowPlaying_WhenMatchSwitchesMediaKind_LateDetailsStayWithOriginalRequest()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var anime = new AnimeEntity
        {
            Id = AnimeId, Title = "Anime title", TotalEpisodes = 12, StatusDetailed = "currently_airing", Genres = ["Drama"]
        };
        var manga = new AnimeEntity
        {
            Id = AnimeId, Title = "Manga title", MediaKind = MediaKind.Manga, Chapters = 20,
            StatusDetailed = "publishing", Genres = ["Adventure"]
        };
        Assert.True((await scenario.Actions.AddToListAsync(anime, UserAnimeStatus.Watching, 2, ct)).Success);
        Assert.True((await scenario.Actions.AddToListAsync(manga, UserAnimeStatus.Watching, 7, ct)).Success);

        var response = new TaskCompletionSource<AnimeEntity?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new Mock<IMalApiService>();
        api.Setup(a => a.GetAnimeDetailsAsync(AnimeId, It.IsAny<CancellationToken>())).Returns(response.Task);
        var tracking = new Mock<ITrackingService>();
        using var vm = new NowPlayingViewModel(tracking.Object, CreateMetadataSettings(), scenario.Library,
            scenario.Actions, Mock.Of<IShikiMetadataService>(), api.Object, ILocalizer.Passthrough);
        var match = anime.Clone();
        vm.MatchedAnime = match;
        match.TotalEpisodes = 0;
        var enrichment = vm.EnsureFullDetailsSafeAsync(match);
        Assert.True(vm.IsLoadingDetails);
        vm.MatchedAnime = manga;
        Assert.False(vm.IsLoadingDetails);
        response.SetResult(new AnimeEntity { Id = AnimeId, Title = anime.Title, Synopsis = "Late anime synopsis" });
        await enrichment.WaitAsync(ct);

        Assert.Same(manga, vm.MatchedAnime);
        Assert.Equal(0, match.TotalEpisodes);
        Assert.NotEqual("Late anime synopsis", match.Synopsis);
        var savedManga = await scenario.Store.GetByIdAsync(AnimeId, MediaKind.Manga, ct);
        Assert.NotNull(savedManga);
        Assert.Equal(7, savedManga.ChaptersRead);
        Assert.True(string.IsNullOrEmpty(savedManga.Synopsis));
        tracking.Verify(t => t.NotifyCurrentMediaMetadataUpdated(anime), Times.Once);
        tracking.Verify(t => t.NotifyCurrentMediaMetadataUpdated(manga), Times.Once);
    }

    [Fact]
    public async Task TorrentEpisodeMark_AdvancesProgressAndRejectsDeletedOrReplacedEntries()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var entry = new AnimeEntity { Id = AnimeId, Title = "Test title", TotalEpisodes = 12 };
        Assert.True((await scenario.Actions.AddToListAsync(entry, UserAnimeStatus.Watching, 2, ct)).Success);
        var filters = Mock.Of<ITorrentFilterRepository>(store => store.GetHiddenAnimeIds() == new HashSet<int>());
        var vm = new TorrentsViewModel(Mock.Of<IRssFeedService>(), scenario.Library, CreateMetadataSettings(),
            filters, Mock.Of<ITorrServerService>(), ILocalizer.Passthrough,
            progressService: scenario.Progress)
        {
            SelectedAnime = entry,
            ActiveOnlineAnime = new OnlineAnimeItem(new StreamingSearchResult
            {
                Id = "example", Title = entry.Title, ProviderName = "Test", Url = "https://test.invalid/anime"
            })
        };
        var third = new OnlineEpisodeItem(new StreamingEpisode { EpisodeNumber = 3, Title = "Episode 3" });
        var fifth = new OnlineEpisodeItem(new StreamingEpisode { EpisodeNumber = 5, Title = "Episode 5" });
        vm.OnlineEpisodes.Add(third);
        vm.OnlineEpisodes.Add(fifth);
        Assert.True(await scenario.Progress.RecordScrobbleAsync(entry, 4, null, ct));
        await vm.MarkEpisodeWatchedCommand.ExecuteAsync(third);
        Assert.Equal(4, entry.Progress);
        Assert.True(third.IsWatched);
        await vm.MarkEpisodeWatchedCommand.ExecuteAsync(fifth);
        Assert.Equal(5, entry.Progress);
        Assert.True(fifth.IsWatched);

        var generation = scenario.Library.GetEntryGeneration(AnimeId, MediaKind.Anime);
        Assert.True((await scenario.Actions.RemoveFromListAsync(AnimeId, ct)).Success);
        await vm.MarkEpisodeWatchedCommand.ExecuteAsync(fifth);
        Assert.False(fifth.IsWatched);
        Assert.Null(await scenario.Store.GetByIdAsync(AnimeId, ct: ct));
        Assert.True((await scenario.Actions.AddToListAsync(entry, UserAnimeStatus.Watching, 1, ct)).Success);
        Assert.False(await scenario.Progress.MarkEpisodeWatchedAsync(new MediaKey(AnimeId, MediaKind.Anime), generation, 8));
        var saved = await scenario.Store.GetByIdAsync(AnimeId, ct: ct);
        Assert.NotNull(saved);
        Assert.Equal(1, saved.Progress);
        Assert.Equal(5, (await scenario.History.GetHistoryAsync(ct: ct)).Count);
        Assert.Equal(5, (await scenario.Queue.GetPendingAsync(ct)).Count);
    }

    private static (AiringInfoService Service, Mock<INotificationService> Notifications) CreateAiringService(
        LibraryScenario scenario, Func<CancellationToken, Task<EpisodeAiringInfo?>> fetch)
    {
        var api = new Mock<IAniListApiService>();
        api.Setup(a => a.GetNextAiringAsync(AnimeId, true, It.IsAny<CancellationToken>()))
            .Returns((int _, bool _, CancellationToken ct) => fetch(ct));
        var notifications = new Mock<INotificationService>();
        return (new AiringInfoService(api.Object, Mock.Of<IShikiApiService>(), CreateMetadataSettings(),
            scenario.Library, scenario.Store, Mock.Of<IAnimeSyncOrchestrator>(), notifications.Object,
            new ImmediateProgressDispatcher(), syncGate: scenario.Gate), notifications);
    }

    private static ISettingsService CreateMetadataSettings()
    {
        var settings = new AppSettings();
        settings.System.AiringSource = EpisodeAiringSource.AniList;
        settings.UI.UseRussianTitles = true;
        return Mock.Of<ISettingsService>(service => service.Current == settings);
    }
}
