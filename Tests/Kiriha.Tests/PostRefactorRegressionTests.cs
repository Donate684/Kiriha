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
using Kiriha.Core.Streaming.Models;
using Kiriha.Core.Streaming.Services;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Core.Tracking.Sync.Models;
using Kiriha.Infrastructure.TorrServer;
using Kiriha.Mpv.UI.ViewModels.Player;
using Kiriha.Services;
using Kiriha.Infrastructure.Settings;
using Kiriha.Utils;
using Kiriha.ViewModels.AnimeDetails;
using Kiriha.ViewModels.Torrents;

namespace Kiriha.Tests;

[Collection("Sequential")]
public sealed class PostRefactorRegressionTests
{
    [Fact]
    public async Task OnlinePlayback_ChangingEpisodeCancelsOlderExtractionAndOpensOnlyTheNewSource()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var oldExtraction = new TaskCompletionSource<IReadOnlyList<StreamingVideo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var streaming = new Mock<IStreamingService>();
        const string firstUrl = "https://fixture.example/embed/first";
        const string secondUrl = "https://fixture.example/video/second.m3u8";
        streaming.Setup(s => s.ResolveStreamAsync(firstUrl, It.IsAny<CancellationToken>())).Returns(oldExtraction.Task);
        streaming.Setup(s => s.ResolveStreamAsync(secondUrl, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        streaming.Setup(s => s.GetEpisodeSourcesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var animeRepo = new Mock<IAnimeRepository>();
        animeRepo.SetupGet(r => r.Collection).Returns(new ObservableCollection<AnimeEntity>());
        var filters = new Mock<ITorrentFilterRepository>();
        filters.Setup(f => f.GetHiddenAnimeIds()).Returns(new HashSet<int>());
        var viewModel = new TorrentsViewModel(Mock.Of<IRssFeedService>(), animeRepo.Object,
            CreateMockSettings().Object, filters.Object, Mock.Of<ITorrServerService>(),
            CreateMockLocalizer().Object, streaming.Object);
        var first = new OnlineEpisodeItem(new StreamingEpisode { EpisodeNumber = 1, Title = "First" });
        var second = new OnlineEpisodeItem(new StreamingEpisode { EpisodeNumber = 2, Title = "Second" });
        var previousSource = new OnlineSourceItem(new StreamingSource { Title = "First source", PlayerUrl = firstUrl });
        var currentSource = new OnlineSourceItem(new StreamingSource { Title = "Second source", PlayerUrl = secondUrl });
        viewModel.ActiveOnlineAnime = new OnlineAnimeItem(new StreamingSearchResult
        {
            Id = "series", Title = "Fixture anime", ProviderName = "Fixture", Url = "https://fixture.example/series"
        });
        viewModel.SelectedOnlineEpisode = first;
        viewModel.OnlineEpisodes.Add(first);
        viewModel.OnlineEpisodes.Add(second);
        viewModel.OnlineSources.Add(previousSource);

        var launched = new List<string?>();
        PlayerProcessHelper.LaunchOverrideForTests = (url, _, _, _, _, _, _, _, _) => launched.Add(url);
        try
        {
            var firstLaunch = viewModel.PlayOnlineSourceAsync(previousSource);
            Assert.True(viewModel.IsPlayerLaunching);
            await viewModel.SelectOnlineEpisodeAsync(second).WaitAsync(timeout.Token);
            Assert.False(viewModel.IsPlayerLaunching);
            oldExtraction.SetResult([new StreamingVideo { Url = "https://fixture.example/video/old.mp4", VideoType = "mp4" }]);
            await firstLaunch.WaitAsync(timeout.Token);
            Assert.Empty(launched);
            Assert.Same(second, viewModel.SelectedOnlineEpisode);

            viewModel.OnlineSources.Add(currentSource);
            await viewModel.PlayOnlineSourceAsync(currentSource).WaitAsync(timeout.Token);
            Assert.Equal(new[] { secondUrl }, launched);
        }
        finally
        {
            oldExtraction.TrySetResult([]);
            viewModel.CloseOnlineEpisodeSelector();
            PlayerProcessHelper.LaunchOverrideForTests = null;
        }
    }

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

        var lease = new Mock<IDisposable>();
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lease.Setup(x => x.Dispose()).Callback(() => released.TrySetResult());
        var addedStatus = new TorrServerTorrentStatus { Hash = hash, Title = "Test" };
        var torrServerMock = new Mock<ITorrServerService>();
        torrServerMock.Setup(x => x.EnsureRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        torrServerMock.SetupGet(x => x.ServerUrl).Returns("http://127.0.0.1:8090");
        torrServerMock.Setup(x => x.PrepareTorrentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TorrServerPreparation("http://127.0.0.1:8090", addedStatus, lease.Object));
        torrServerMock.Setup(x => x.WaitForFilesAsync(hash, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TorrServerFileItem>());
        torrServerMock.Setup(x => x.GetStreamUrl(hash, -1, It.IsAny<string>()))
            .Returns($"http://127.0.0.1:8090/stream/test.mkv?link={hash}&index=-1&play");

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
            lease.Verify(x => x.Dispose(), Times.Never);

            // Explicitly cancelling player launch while awaiting wakeup MUST drop the launched torrent
            vm.CancelPlayerLaunchingCommand.Execute(null);
            await released.Task.WaitAsync(TimeSpan.FromSeconds(2));

            await playTask;
            Assert.False(vm.IsPlayerLaunching);
        }
        finally
        {
            PlayerProcessHelper.LaunchOverrideForTests = null;
        }
    }

    [Fact]
    public async Task TorrentsViewModel_LateMetadataFromReplacedLaunch_DoesNotOpenPreviousFile()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var firstWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateFiles = new TaskCompletionSource<IReadOnlyList<TorrServerFileItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newLaunch = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = new Mock<ITorrServerService>();
        tracker.Setup(t => t.EnsureRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        tracker.SetupGet(t => t.ServerUrl).Returns("http://127.0.0.1");
        tracker.Setup(t => t.PrepareTorrentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string title, CancellationToken _) =>
                new TorrServerPreparation("http://127.0.0.1", new TorrServerTorrentStatus { Hash = title }, Mock.Of<IDisposable>()));
        tracker.Setup(t => t.WaitForFilesAsync("previous", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                firstWaiting.TrySetResult();
                return lateFiles.Task; // A delayed response can still arrive after cancellation.
            });
        tracker.Setup(t => t.WaitForFilesAsync("current", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        tracker.Setup(t => t.GetStreamUrl(It.IsAny<string>(), -1, It.IsAny<string>())).Returns("http://127.0.0.1/stream");
        var library = new Mock<IAnimeRepository>();
        library.SetupGet(r => r.Collection).Returns(new ObservableCollection<AnimeEntity>());
        var filters = Mock.Of<ITorrentFilterRepository>(repository => repository.GetHiddenAnimeIds() == new HashSet<int>());
        var viewModel = new TorrentsViewModel(Mock.Of<IRssFeedService>(), library.Object, CreateMockSettings().Object,
            filters, tracker.Object, CreateMockLocalizer().Object);
        var launchedHashes = new List<string?>();
        PlayerProcessHelper.LaunchOverrideForTests = (_, _, _, _, _, hash, _, _, _) =>
        {
            launchedHashes.Add(hash);
            newLaunch.TrySetResult(hash);
        };

        try
        {
            var first = viewModel.PlayTorrentAsync(new TorrentEntity { Title = "previous", MagnetLink = "magnet:?previous" });
            await firstWaiting.Task.WaitAsync(timeout.Token);
            var second = viewModel.PlayTorrentAsync(new TorrentEntity { Title = "current", MagnetLink = "magnet:?current" });
            Assert.Equal("current", await newLaunch.Task.WaitAsync(timeout.Token));
            lateFiles.SetResult([]);
            await first.WaitAsync(timeout.Token);
            Assert.Equal(new[] { "current" }, launchedHashes);
            Assert.True(viewModel.IsPlayerLaunching);
            Assert.Null(viewModel.StreamingErrorMessage);
            viewModel.CancelPlayerLaunching();
            await second.WaitAsync(timeout.Token);
        }
        finally
        {
            lateFiles.TrySetResult([]);
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
        var lease = new Mock<IDisposable>();
        torrServerMock.Setup(x => x.PrepareTorrentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TorrServerPreparation("http://127.0.0.1:8090", addedStatus, lease.Object));
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
            lease.Verify(x => x.Dispose(), Times.Once);

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
        animeRepoMock.Setup(x => x.RefreshEditorDetailsAsync(original, fullFromApi, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AnimeEditFields.From(fullFromApi), 1L));
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

            var progressMock = new Mock<IProgressUpdateService>();
            var notificationMock = new Mock<INotificationService>();
            var bgMock = new Mock<IBackgroundTaskSupervisor>();
            bgMock.Setup(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
                .Callback<string, Func<CancellationToken, Task>, CancellationToken>((_, fn, ct) => fn(ct).GetAwaiter().GetResult());

            var dispatcher = new SyncUiDispatcher();
            using var scrobble = new ScrobbleService(
                progressMock.Object,
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

            progressMock.Verify(x => x.RecordScrobbleAsync(anime, 5, It.IsAny<UserAnimeStatus?>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            try { File.Delete(tempSettingsPath); } catch { }
        }
    }

    [Fact]
    public void PlayerViewModel_LoadVideo_ChangesTorrentHashAndClearsStaleStreamHeaders()
    {
        const string hash1 = "1111111111111111111111111111111111111111";
        const string hash2 = "2222222222222222222222222222222222222222";

        var torrServerMock = new Mock<ITorrServerService>();

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
        torrServerMock.Verify(x => x.DropTorrentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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

    [Fact]
    public void TorrServer_DownloadScript_And_CandidateCheck_RejectIncompatibleSiloReleases()
    {
        var method = typeof(TorrServerService).GetMethod("IsCompatibleMatriXCandidate", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var tempRoot = Path.Combine(Path.GetTempPath(), $"kiriha_ts_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var siloExe = Path.Combine(tempRoot, "torrserver.exe");
            File.WriteAllBytes(siloExe, [0x4D, 0x5A]);
            File.WriteAllText(Path.Combine(tempRoot, "version.txt"), "Silo.2");
            Assert.False((bool)method.Invoke(null, [siloExe])!);

            File.WriteAllText(Path.Combine(tempRoot, "version.txt"), "MatriX.150");
            Assert.True((bool)method.Invoke(null, [siloExe])!);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }

        // Verify scripts/download-torrserver.ps1 never queries /releases/latest blindly and filters for ^MatriX\.
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        string? scriptPath = null;
        for (int i = 0; i < 6 && current != null; i++)
        {
            var candidate = Path.Combine(current.FullName, "scripts", "download-torrserver.ps1");
            if (File.Exists(candidate))
            {
                scriptPath = candidate;
                break;
            }
            current = current.Parent;
        }

        Assert.NotNull(scriptPath);
        var scriptContent = File.ReadAllText(scriptPath);
        Assert.DoesNotContain("/releases/latest\"", scriptContent, StringComparison.Ordinal);
        Assert.Contains("^MatriX\\.", scriptContent, StringComparison.Ordinal);
        Assert.Contains("--version", scriptContent, StringComparison.Ordinal);
    }
}
