using System.Collections.ObjectModel;
using System.Net;
using System.Reflection;
using System.Text;
using Moq;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Dialogs;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Domain.Models.TorrServer;
using Kiriha.Core.Streaming.Abstractions;
using Kiriha.Core.Streaming.Services;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Core.Tracking.Sync.Models;
using Kiriha.Infrastructure.TorrServer;
using Kiriha.Mpv.UI.ViewModels.Player;
using Kiriha.Services;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Settings;
using Kiriha.Utils;
using Kiriha.ViewModels.AnimeDetails;
using Kiriha.ViewModels.Torrents;

namespace Kiriha.Tests;

[Collection("Sequential")]
public sealed class PostRefactorRegressionTests
{
    private sealed class DelegatingHandlerStub(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request);
    }

    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
        public Task InvokeAsync(Func<Task> action) => action();
        public void Post(Action action) => action();
    }

    private static Mock<ISettingsService> CreateMockSettings()
    {
        var appSettings = new AppSettings();
        var mock = new Mock<ISettingsService>();
        mock.SetupGet(x => x.Current).Returns(appSettings);
        return mock;
    }

    private static Mock<ILocalizer> CreateMockLocalizer()
    {
        var mock = new Mock<ILocalizer>();
        mock.Setup(x => x.GetLoc(It.IsAny<string>())).Returns((string k) => k);
        mock.Setup(x => x.GetLoc(It.IsAny<string>(), It.IsAny<object?[]>())).Returns((string k, object?[] _) => k);
        return mock;
    }

    [Fact]
    public async Task TorrentsViewModel_PlayTorrentAsync_WhenMetadataNotResolvedYet_LaunchesPlayerFallbackAndPreservesLaunchedHash()
    {
        const string hash = "0123456789abcdef0123456789abcdef01234567";

        var rssMock = new Mock<IRssFeedService>();
        var animeRepoMock = new Mock<IAnimeRepository>();
        animeRepoMock.SetupGet(x => x.Collection).Returns(new ObservableCollection<AnimeEntity>());
        var filterRepoMock = new Mock<ITorrentFilterRepository>();
        filterRepoMock.Setup(x => x.GetHiddenAnimeIds()).Returns(new HashSet<int>());

        var droppedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var addedStatus = new TorrServerTorrentStatus { Hash = hash, Title = "Test" };
        var torrServerMock = new Mock<ITorrServerService>();
        torrServerMock.Setup(x => x.EnsureRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        torrServerMock.Setup(x => x.AddTorrentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(addedStatus);
        torrServerMock.Setup(x => x.WaitForFilesAsync(hash, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TorrServerFileItem>());
        torrServerMock.Setup(x => x.GetStreamUrl(hash, -1, It.IsAny<string>()))
            .Returns($"http://127.0.0.1:8090/stream/test.mkv?link={hash}&index=-1&play");
        torrServerMock.Setup(x => x.DropTorrentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((h, _) => droppedTcs.TrySetResult(h))
            .Returns(Task.CompletedTask);

        string? launchedUrl = null;
        string? launchedHash = null;
        var launchedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        PlayerProcessHelper.LaunchOverrideForTests = (url, _, _, _, _, tHash, _, _, _) =>
        {
            launchedUrl = url;
            launchedHash = tHash;
            launchedTcs.TrySetResult(true);
        };

        try
        {
            var vm = new TorrentsViewModel(
                rssMock.Object,
                animeRepoMock.Object,
                CreateMockSettings().Object,
                filterRepoMock.Object,
                torrServerMock.Object,
                CreateMockLocalizer().Object);

            var torrent = new TorrentEntity
            {
                Title = "[SubsPlease] Sousou no Frieren - 05 (1080p) [ABCD1234].mkv",
                AnimeTitle = "Sousou no Frieren",
                Episode = "5",
                MagnetLink = $"magnet:?xt=urn:btih:{hash}"
            };

            var playTask = vm.PlayTorrentCommand.ExecuteAsync(torrent);
            await launchedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.NotNull(launchedUrl);
            Assert.Contains("index=-1", launchedUrl, StringComparison.Ordinal);
            Assert.Equal(hash, launchedHash);
            Assert.Null(vm.StreamingErrorMessage);
            Assert.True(vm.IsPlayerLaunching);

            // Closing the batch episode selector must NOT drop the already-launched playback torrent
            vm.CloseEpisodeSelectorCommand.Execute(null);
            torrServerMock.Verify(x => x.DropTorrentAsync(hash, It.IsAny<CancellationToken>()), Times.Never);

            // Explicitly cancelling player launch while awaiting wakeup MUST drop the launched torrent
            vm.CancelPlayerLaunchingCommand.Execute(null);
            var droppedHash = await droppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(hash, droppedHash);

            await playTask;
            Assert.False(vm.IsPlayerLaunching);
        }
        finally
        {
            PlayerProcessHelper.LaunchOverrideForTests = null;
        }
    }

    [Fact]
    public async Task TorrentsViewModel_PlayTorrentAsync_WhenNonVideoFilesOnly_DropsTorrentAndSetsStreamingError()
    {
        const string hash = "fedcba9876543210fedcba9876543210fedcba98";

        var rssMock = new Mock<IRssFeedService>();
        var animeRepoMock = new Mock<IAnimeRepository>();
        animeRepoMock.SetupGet(x => x.Collection).Returns(new ObservableCollection<AnimeEntity>());
        var filterRepoMock = new Mock<ITorrentFilterRepository>();
        filterRepoMock.Setup(x => x.GetHiddenAnimeIds()).Returns(new HashSet<int>());

        var addedStatus = new TorrServerTorrentStatus { Hash = hash, Title = "OST Archive" };
        IReadOnlyList<TorrServerFileItem> nonVideoFiles =
        [
            new TorrServerFileItem { Id = 1, Path = "readme.txt", Length = 1024 },
            new TorrServerFileItem { Id = 2, Path = "scans.zip", Length = 50_000_000 }
        ];

        var torrServerMock = new Mock<ITorrServerService>();
        torrServerMock.Setup(x => x.EnsureRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        torrServerMock.Setup(x => x.AddTorrentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(addedStatus);
        torrServerMock.Setup(x => x.WaitForFilesAsync(hash, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nonVideoFiles);
        torrServerMock.Setup(x => x.DropTorrentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        bool launched = false;
        PlayerProcessHelper.LaunchOverrideForTests = (_, _, _, _, _, _, _, _, _) => launched = true;

        try
        {
            var vm = new TorrentsViewModel(
                rssMock.Object,
                animeRepoMock.Object,
                CreateMockSettings().Object,
                filterRepoMock.Object,
                torrServerMock.Object,
                CreateMockLocalizer().Object);

            var torrent = new TorrentEntity
            {
                Title = "OST Archive",
                MagnetLink = $"magnet:?xt=urn:btih:{hash}"
            };

            await vm.PlayTorrentCommand.ExecuteAsync(torrent);

            Assert.False(launched);
            Assert.False(string.IsNullOrWhiteSpace(vm.StreamingErrorMessage));
            torrServerMock.Verify(x => x.DropTorrentAsync(hash, It.IsAny<CancellationToken>()), Times.Once);

            vm.DismissStreamingErrorCommand.Execute(null);
            Assert.Null(vm.StreamingErrorMessage);
        }
        finally
        {
            PlayerProcessHelper.LaunchOverrideForTests = null;
        }
    }

    [Fact]
    public async Task TorrServerService_ExternalInstanceWithActiveTorrents_SkipsDestructiveSettingsSet_AndParsesMixedJsonNumbers()
    {
        int settingsSetCalls = 0;

        var handler = new DelegatingHandlerStub(async req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            if (path == "/echo")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("MatriX.136", Encoding.UTF8, "text/plain")
                };
            }

            if (path == "/torrents")
            {
                var body = req.Content != null ? await req.Content.ReadAsStringAsync() : string.Empty;
                if (body.Contains("\"list\"", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[{\"hash\":\"abc123\"}]", Encoding.UTF8, "application/json")
                    };
                }

                if (body.Contains("\"get\"", StringComparison.Ordinal))
                {
                    const string statusJson = """
                    {
                      "hash": "abc123",
                      "title": "Mixed Types Torrent",
                      "stat": "3",
                      "stat_string": "Torrent working",
                      "torrent_size": "2147483648",
                      "loaded_size": 104857600,
                      "preloaded_bytes": "52428800",
                      "preload_size": 209715200,
                      "download_speed": "1572864.5",
                      "upload_speed": 0,
                      "total_peers": "42",
                      "active_peers": 12,
                      "file_stats": [
                        { "id": 1, "path": "Anime/Episode 01.mkv", "length": "734003200" }
                      ]
                    }
                    """;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(statusJson, Encoding.UTF8, "application/json")
                    };
                }
            }

            if (path == "/settings")
            {
                var body = req.Content != null ? await req.Content.ReadAsStringAsync() : string.Empty;
                if (body.Contains("\"set\"", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref settingsSetCalls);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"CacheSize\":209715200,\"UseDisk\":false}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        using var service = new TorrServerService(CreateMockSettings().Object, httpClient);

        bool ready = await service.EnsureRunningAsync();
        Assert.True(ready);
        Assert.Equal(0, settingsSetCalls);

        var status = await service.GetTorrentAsync("abc123");
        Assert.NotNull(status);
        Assert.Equal(3, status.Stat);
        Assert.Equal(2147483648L, status.TorrentSize);
        Assert.Equal(52428800L, status.PreloadedBytes);
        Assert.Equal(42, status.TotalPeers);
        Assert.Single(status.Files);
        Assert.Equal(734003200L, status.Files[0].Length);
    }

    [Fact]
    public async Task MangaCompositeKey_RemoveAnimeAsync_RemovesOnlyMangaAndExecutesRemoveMangaOnTracker()
    {
        var trackerMock = new Mock<ITrackerService>();
        trackerMock.SetupGet(x => x.Name).Returns("MyAnimeList");
        trackerMock.SetupGet(x => x.IsEnabled).Returns(true);
        trackerMock.Setup(x => x.RemoveMangaAsync(777, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.Success);

        SyncTaskEntity? capturedEntity = null;
        var syncTaskRepoMock = new Mock<ISyncTaskRepository>();
        syncTaskRepoMock.Setup(x => x.AddAsync(It.IsAny<SyncTaskEntity>(), It.IsAny<CancellationToken>()))
            .Callback<SyncTaskEntity, CancellationToken>((e, _) =>
            {
                e.Id = 42;
                capturedEntity = e;
            })
            .ReturnsAsync(42);

        var dbInitMock = new Mock<IDatabaseInitializer>();
        dbInitMock.SetupGet(x => x.InitializationTask).Returns(Task.CompletedTask);
        var historyMock = new Mock<IHistoryService>();
        var bgMock = new Mock<IBackgroundTaskSupervisor>();
        var notifyMock = new Mock<INotificationService>();

        var syncManager = new SyncManager(
            [trackerMock.Object],
            syncTaskRepoMock.Object,
            dbInitMock.Object,
            historyMock.Object,
            bgMock.Object,
            notifyMock.Object);

        var animeRepoMock = new Mock<IAnimeRepository>();
        var userRepoMock = new Mock<IUserAnimeRepository>();
        var dispatcher = new SyncUiDispatcher();

        var progressService = new AnimeProgressService(
            animeRepoMock.Object,
            userRepoMock.Object,
            syncManager,
            historyMock.Object,
            dispatcher);

        await progressService.RemoveAnimeAsync(777, MediaKind.Manga);

        // Must call composite MediaKind.Manga overload, NOT 1-arg Anime overload
        animeRepoMock.Verify(x => x.RemoveAnimeLocalAsync(777, MediaKind.Manga), Times.Once);
        animeRepoMock.Verify(x => x.RemoveAnimeLocalAsync(777), Times.Never);

        Assert.NotNull(capturedEntity);
        Assert.Equal(nameof(SyncTaskType.Remove), capturedEntity.Type);
        Assert.NotNull(capturedEntity.Payload);
        var restoredEntity = System.Text.Json.JsonSerializer.Deserialize<AnimeEntity>(capturedEntity.Payload);
        Assert.NotNull(restoredEntity);
        Assert.Equal(MediaKind.Manga, restoredEntity.MediaKind);

        var executeTask = new SyncTask
        {
            Id = capturedEntity.Id,
            AnimeId = capturedEntity.AnimeId,
            MediaKind = MediaKind.Manga,
            Type = SyncTaskType.Remove
        };

        var executeMethod = typeof(SyncManager).GetMethod("ExecuteTaskAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(executeMethod);

        var (success, didExecute) = await (Task<(bool Success, bool Executed)>)executeMethod.Invoke(
            syncManager,
            [executeTask, CancellationToken.None])!;

        Assert.True(success);
        Assert.True(didExecute);
        trackerMock.Verify(x => x.RemoveMangaAsync(777, It.IsAny<CancellationToken>()), Times.Once);
        trackerMock.Verify(x => x.RemoveAnimeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void AnimeEntity_MergeFullDetails_PreservesChaptersVolumesAndShallowNoneUserStatus()
    {
        var shallow = new AnimeEntity
        {
            Id = 501,
            MediaKind = MediaKind.Manga,
            Title = "Vagabond",
            Status = UserAnimeStatus.None
        };

        var full = new AnimeEntity
        {
            Id = 501,
            MediaKind = MediaKind.Manga,
            Title = "Vagabond",
            Chapters = 327,
            Volumes = 37,
            Status = UserAnimeStatus.Watching,
            ChaptersRead = 100,
            VolumesRead = 12,
            Score = "10"
        };

        shallow.MergeFullDetails(full);

        Assert.Equal(327, shallow.Chapters);
        Assert.Equal(37, shallow.Volumes);
        Assert.Equal(UserAnimeStatus.Watching, shallow.Status);
        Assert.Equal(100, shallow.ChaptersRead);
        Assert.Equal(12, shallow.VolumesRead);
        Assert.Equal("10", shallow.Score);
    }

    [Fact]
    public async Task AnimeDetailsViewModel_WhenShallowNoneEntityHydratesStatus_EditorHasChangesRemainsFalse()
    {
        var original = new AnimeEntity
        {
            Id = 909,
            MediaKind = MediaKind.Anime,
            Title = "Frieren",
            Status = UserAnimeStatus.None
        };
        var clone = original.Clone();

        var fullFromApi = new AnimeEntity
        {
            Id = 909,
            MediaKind = MediaKind.Anime,
            Title = "Frieren",
            TotalEpisodes = 28,
            Status = UserAnimeStatus.Watching,
            Progress = 6,
            Score = "9"
        };

        var malMock = new Mock<IMalApiService>();
        malMock.Setup(x => x.GetAnimeDetailsAsync(909, It.IsAny<CancellationToken>())).ReturnsAsync(fullFromApi);
        var shikiApiMock = new Mock<IShikiApiService>();
        var animeRepoMock = new Mock<IAnimeRepository>();
        animeRepoMock.SetupGet(x => x.Collection).Returns(new ObservableCollection<AnimeEntity>());
        var franchiseMock = new Mock<IFranchiseService>();
        var listActionMock = new Mock<IAnimeListActionService>();
        var dialogsMock = new Mock<IDialogService>();
        var settingsMock = CreateMockSettings();

        var editor = new AnimeEditViewModel(original, clone, listActionMock.Object);
        var metadata = new AnimeMetadataViewModel(clone, malMock.Object);

        using var detailsVm = new AnimeDetailsViewModel(
            clone,
            original,
            editor,
            metadata,
            settingsMock.Object,
            dialogsMock.Object,
            shikiApiMock.Object,
            animeRepoMock.Object,
            malMock.Object,
            franchiseMock.Object,
            listActionMock.Object);

        for (int i = 0; i < 20 && detailsVm.IsLoading; i++)
        {
            await Task.Delay(15);
        }

        Assert.Equal(UserAnimeStatus.Watching, clone.Status);
        Assert.Equal(6, clone.Progress);
        Assert.False(editor.HasChanges);
    }

    [Fact]
    public void ScrobbleService_StartScrobble_WhenEpisodeIsBatchRange_FallsBackToOriginalTitleEpisode()
    {
        var tempSettingsPath = Path.GetTempFileName();
        try
        {
            var settingsService = new SettingsService(tempSettingsPath);
            settingsService.Update(s =>
            {
                s.System.Scrobbler.Enabled = true;
                s.System.Scrobbler.DelaySeconds = 0;
                s.System.Scrobbler.NotifyOnSkippedEpisode = true;
            }, save: false);

            var progressMock = new Mock<AnimeProgressService>(null!, null!, null!, null!, null!);
            var historyMock = new Mock<HistoryService>(null!);
            var notificationMock = new Mock<NotificationService>(null!, null!);
            var bgMock = new Mock<IBackgroundTaskSupervisor>();
            bgMock.Setup(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
                .Callback<string, Func<CancellationToken, Task>, CancellationToken>((_, fn, ct) => fn(ct).GetAwaiter().GetResult());

            var dispatcher = new SyncUiDispatcher();
            using var scrobble = new ScrobbleService(
                progressMock.Object,
                historyMock.Object,
                settingsService,
                notificationMock.Object,
                bgMock.Object,
                dispatcher,
                CreateMockLocalizer().Object,
                new Mock<IScrobbleProgressStore>().Object);

            var anime = new AnimeEntity
            {
                Id = 1234,
                Title = "Sousou no Frieren",
                Status = UserAnimeStatus.Watching,
                Progress = 4,
                TotalEpisodes = 28
            };

            var media = new ParsedMedia
            {
                Episode = "01-28",
                OriginalTitle = "[SubsPlease] Sousou no Frieren - 05 (1080p).mkv",
                IsPlaying = true
            };

            scrobble.StartScrobble(media, anime);

            progressMock.Verify(x => x.UpdateProgressAsync(anime, 5, It.IsAny<UserAnimeStatus?>()), Times.Once);
        }
        finally
        {
            try { File.Delete(tempSettingsPath); } catch { }
        }
    }

    [Fact]
    public async Task PlayerViewModel_LoadVideo_DropsOldTorrentHashAndClearsStaleStreamHeaders()
    {
        const string hash1 = "1111111111111111111111111111111111111111";
        const string hash2 = "2222222222222222222222222222222222222222";

        var droppedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var torrServerMock = new Mock<ITorrServerService>();
        torrServerMock.Setup(x => x.DropTorrentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((h, _) => droppedTcs.TrySetResult(h))
            .Returns(Task.CompletedTask);

        var initialHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer token123" };
        var meta1 = new PlayerMediaMetadata("Ep 1", "Title", "Title", "1", 100, "Title", hash1);

        using var vm = new PlayerViewModel(
            $"http://127.0.0.1:8090/stream/ep1.mkv?link={hash1}&index=1&play",
            meta1,
            metadataResolver: null,
            settingsService: CreateMockSettings().Object,
            localizer: CreateMockLocalizer().Object,
            torrServer: torrServerMock.Object,
            headers: initialHeaders);

        Assert.Equal(hash1, vm.TorrentHash);
        Assert.NotNull(vm.StreamHeaders);

        var meta2 = new PlayerMediaMetadata("Ep 2", "Title", "Title", "2", 100, "Title", hash2);
        vm.LoadVideo(
            $"http://127.0.0.1:8090/stream/ep2.mkv?link={hash2}&index=2&play",
            subtitleUrl: null,
            metadata: meta2,
            headers: null);

        Assert.Equal(hash2, vm.TorrentHash);
        Assert.Null(vm.StreamHeaders);
        var droppedHash = await droppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(hash1, droppedHash);
    }

    [Fact]
    public async Task OnlineScorer_RssFeed_And_StreamingService_HandlePrefixSequelsRomanNumeralsAndSignedUrls()
    {
        // 1. Prefix sequel token penalty in OnlineRelevanceScorer
        int prefixSequelScore = OnlineRelevanceScorer.Score("Наруто", "2 сезон: Наруто");
        Assert.Equal(5000, prefixSequelScore);

        // 2. Roman numeral sequel matching & null-StartYear fallback in RssFeedService
        var overlordS1 = new AnimeEntity { Id = 1, MediaKind = MediaKind.Anime, Title = "Overlord", Status = UserAnimeStatus.Watching };
        var overlordS2 = new AnimeEntity { Id = 2, MediaKind = MediaKind.Anime, Title = "Overlord II", Status = UserAnimeStatus.Watching };
        var matchedSequel = RssFeedService.MatchActiveAnime([overlordS1, overlordS2], "Overlord", parsedSeason: 2);
        Assert.NotNull(matchedSequel);
        Assert.Equal(2, matchedSequel.Id);

        var shallowWatching = new AnimeEntity { Id = 3, MediaKind = MediaKind.Anime, Title = "Dandadan", StartYear = null, Status = UserAnimeStatus.Watching };
        var matchedYearFallback = RssFeedService.MatchActiveAnime([shallowWatching], "Dandadan", parsedSeason: null, parsedYear: 2024);
        Assert.NotNull(matchedYearFallback);
        Assert.Equal(3, matchedYearFallback.Id);

        // 3. Protocol-relative + query-string direct HLS URL in StreamingService
        var streaming = new StreamingService(Array.Empty<IStreamingProvider>(), Array.Empty<IVideoExtractor>());
        var videos = await streaming.ResolveStreamAsync("//cdn.example.com/hls/master.m3u8?Policy=xyz&Key-Pair-Id=1");
        Assert.Single(videos);
        Assert.Equal("https://cdn.example.com/hls/master.m3u8?Policy=xyz&Key-Pair-Id=1", videos[0].Url);
        Assert.Equal("m3u8", videos[0].VideoType);
    }
}
