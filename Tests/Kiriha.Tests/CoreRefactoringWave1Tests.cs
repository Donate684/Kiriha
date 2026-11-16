using System.Reflection;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Services;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Data.Core;
using Kiriha.Services.Image;
using Kiriha.Core.Tracking.Metadata;
using Kiriha.Core.Tracking.Library;
using Kiriha.Data.Repository;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed class CoreRefactoringWave1Tests
{
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

    [Fact]
    public async Task AnimeSyncOrchestrator_StatusDropGuard_AbortsWhenDropExceeds30Percent()
    {
        // Local has 20 Completed items; remote returns only 12 (40% drop > 30% max allowed drop).
        // Previously, inverted math checked incomingRatio (0.60) < 0.30 and falsely let a 40% wipeout pass!
        var localItems = Enumerable.Range(1, 20)
            .Select(i => new AnimeEntity
            {
                Id = i,
                MediaKind = MediaKind.Anime,
                Title = $"Anime {i}",
                Status = UserAnimeStatus.Completed
            })
            .ToList();

        var partialRemote = localItems.Take(12).Select(x => x.Clone()).ToList();

        var mockAnimeRepo = new Mock<IAnimeRepository>();
        mockAnimeRepo.Setup(r => r.GetSnapshotAsync(It.IsAny<MediaKind[]>()))
            .ReturnsAsync(localItems);

        var mockUserRepo = new Mock<IUserAnimeRepository>();
        var mockRecCache = new Mock<IRecognitionCache>();

        var mockTracker = new Mock<ITrackerService>();
        mockTracker.SetupGet(t => t.Name).Returns("MyAnimeList");
        mockTracker.SetupGet(t => t.TrackerId).Returns(TrackerConstants.Ids.Mal);
        mockTracker.SetupGet(t => t.IsEnabled).Returns(true);
        mockTracker.Setup(t => t.GetUserAnimeListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(partialRemote);

        var orchestrator = new AnimeSyncOrchestrator(
            mockAnimeRepo.Object,
            mockUserRepo.Object,
            [mockTracker.Object],
            mockRecCache.Object);

        bool synced = await orchestrator.SyncWithTrackersAsync();

        Assert.False(synced);
        mockUserRepo.Verify(
            r => r.SyncFromRemoteAsync(It.IsAny<IEnumerable<AnimeEntity>>(), It.IsAny<MediaKind[]?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AnimeSyncOrchestrator_PreservesPendingOfflineMutationsAndHonorsPendingRemoves()
    {
        var uiDispatcher = new SyncUiDispatcher();
        var mockUserRepo = new Mock<IUserAnimeRepository>();
        var mockDbInit = new Mock<IDatabaseInitializer>();
        mockDbInit.SetupGet(d => d.InitializationTask).Returns(Task.CompletedTask);
        var mockBg = new Mock<IBackgroundTaskSupervisor>();
        var mockRecCache = new Mock<IRecognitionCache>();

        var animeRepo = new AnimeRepository(mockUserRepo.Object, mockDbInit.Object, mockBg.Object, uiDispatcher, mockRecCache.Object);

        // 1. Item 101: edited offline to Progress = 9 (remote still says Progress = 4)
        var editedOffline = new AnimeEntity
        {
            Id = 101,
            MediaKind = MediaKind.Anime,
            Title = "Offline Watched",
            Status = UserAnimeStatus.Watching,
            Progress = 9
        };
        // 2. Item 102: added offline (not on remote yet)
        var addedOffline = new AnimeEntity
        {
            Id = 102,
            MediaKind = MediaKind.Anime,
            Title = "Offline Added",
            Status = UserAnimeStatus.PlanToWatch,
            Progress = 0
        };
        animeRepo.AddToCollection(editedOffline);
        animeRepo.AddToCollection(addedOffline);

        // Remote has stale Progress = 4 for 101, lacks 102, and still has 103 (which has a pending Remove task)
        var remoteList = new List<AnimeEntity>
        {
            new()
            {
                Id = 101,
                MediaKind = MediaKind.Anime,
                Title = "Offline Watched",
                Status = UserAnimeStatus.Watching,
                Progress = 4,
                Synopsis = "Enriched remote synopsis"
            },
            new()
            {
                Id = 103,
                MediaKind = MediaKind.Anime,
                Title = "Deleted Offline",
                Status = UserAnimeStatus.Watching,
                Progress = 2
            }
        };

        var mockSyncTasks = new Mock<ISyncTaskRepository>();
        mockSyncTasks.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SyncTaskEntity { Id = 1, AnimeId = 101, MediaKind = MediaKind.Anime, Type = "UpdateProgress", Progress = 9 },
                new SyncTaskEntity { Id = 2, AnimeId = 102, MediaKind = MediaKind.Anime, Type = "FullUpdate", Progress = 0 },
                new SyncTaskEntity { Id = 3, AnimeId = 103, MediaKind = MediaKind.Anime, Type = "Remove" }
            ]);

        var mockTracker = new Mock<ITrackerService>();
        mockTracker.SetupGet(t => t.Name).Returns("MyAnimeList");
        mockTracker.SetupGet(t => t.TrackerId).Returns(TrackerConstants.Ids.Mal);
        mockTracker.SetupGet(t => t.IsEnabled).Returns(true);
        mockTracker.Setup(t => t.GetUserAnimeListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteList);

        var orchestrator = new AnimeSyncOrchestrator(
            animeRepo,
            mockUserRepo.Object,
            [mockTracker.Object],
            mockRecCache.Object,
            syncTaskRepo: mockSyncTasks.Object);

        bool synced = await orchestrator.SyncWithTrackersAsync();

        Assert.True(synced);
        Assert.Equal(9, animeRepo.FindById(101, MediaKind.Anime)?.Progress);
        Assert.Equal("Enriched remote synopsis", animeRepo.FindById(101, MediaKind.Anime)?.Synopsis);
        Assert.NotNull(animeRepo.FindById(102, MediaKind.Anime));
        Assert.Null(animeRepo.FindById(103, MediaKind.Anime));
    }

    [Fact]
    public async Task SyncManager_OlderFullUpdateRetryingAfterNewerProgress_DoesNotRollbackProgress()
    {
        AnimeEntity? sentFullItem = null;
        var mockTracker = new Mock<ITrackerService>();
        mockTracker.SetupGet(t => t.Name).Returns("AniList");
        mockTracker.SetupGet(t => t.TrackerId).Returns(TrackerConstants.Ids.AniList);
        mockTracker.SetupGet(t => t.IsEnabled).Returns(true);
        mockTracker
            .Setup(t => t.SaveFullListStatusAsync(It.IsAny<AnimeEntity>(), It.IsAny<CancellationToken>()))
            .Callback<AnimeEntity, CancellationToken>((item, _) => sentFullItem = item)
            .ReturnsAsync(SyncOutcome.Success);

        int nextDbId = 10;
        var mockRepo = new Mock<ISyncTaskRepository>();
        mockRepo
            .Setup(r => r.AddAsync(It.IsAny<SyncTaskEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => nextDbId++);

        var mockDbInit = new Mock<IDatabaseInitializer>();
        mockDbInit.SetupGet(d => d.InitializationTask).Returns(Task.CompletedTask);
        var mockHistory = new Mock<IHistoryService>();
        var mockBg = new Mock<IBackgroundTaskSupervisor>();

        var syncManager = new SyncManager(
            [mockTracker.Object],
            mockRepo.Object,
            mockDbInit.Object,
            mockHistory.Object,
            mockBg.Object);

        // Task 10: FullUpdate with Progress = 3 (imagine it failed transiently and is retrying later)
        var staleItem = new AnimeEntity
        {
            Id = 555,
            MediaKind = MediaKind.Anime,
            Title = "Sousou no Frieren",
            Status = UserAnimeStatus.Watching,
            Progress = 3,
            Notes = "Important note"
        };
        await syncManager.EnqueueFullUpdateAsync(staleItem);

        // Task 11: Newer UpdateProgress with Progress = 4
        await syncManager.EnqueueUpdateAsync(555, MediaKind.Anime, progress: 4);

        var highQueueField = typeof(SyncManager).GetField("_highPriorityQueue", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var queuedTasks = ((System.Collections.IEnumerable)highQueueField.GetValue(syncManager)!).Cast<object>().ToList();
        var olderFullUpdateTask = queuedTasks[0];

        var executeMethod = typeof(SyncManager).GetMethod("ExecuteTaskAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var resultTask = (Task<(bool Success, bool Executed)>)executeMethod.Invoke(syncManager, [olderFullUpdateTask, CancellationToken.None])!;
        var (success, executed) = await resultTask;

        Assert.True(success);
        Assert.True(executed);
        Assert.NotNull(sentFullItem);
        Assert.Equal(4, sentFullItem!.Progress);
        Assert.Equal("Important note", sentFullItem.Notes);
    }

    [Fact]
    public async Task SeasonalHiddenRepository_TransientFailureDuringEnsureInitialized_RecoversWhenDbBecomesReady()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;

        bool simulateUnready = true;
        var mockFactory = new Mock<IDbContextFactory<AppDbContext>>();
        mockFactory.Setup(f => f.CreateDbContext())
            .Returns(() =>
            {
                if (simulateUnready)
                    throw new InvalidOperationException("Database migrating");
                return new AppDbContext(options);
            });

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.HiddenSeasonalAnime.Add(new HiddenSeasonalAnime { AnimeId = 777, CreatedAt = DateTime.UtcNow });
            await setup.SaveChangesAsync();
        }

        var repo = new SeasonalHiddenRepository(mockFactory.Object);

        // 1. Early read while DB is migrating returns false without throwing AND does not poison _initialized
        Assert.False(repo.IsHidden(777));

        // 2. Once DB is ready, subsequent read loads actual rows from SQLite
        simulateUnready = false;
        Assert.True(repo.IsHidden(777));
    }

    [Fact]
    public async Task ShikiMetadataService_CancelledWaitAsync_DoesNotCorruptSemaphoreCount()
    {
        var settings = new AppSettings();
        settings.UI.UseRussianTitles = true;
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.SetupGet(s => s.Current).Returns(settings);

        var mockMetaRepo = new Mock<IMetadataRepository>();
        mockMetaRepo.Setup(r => r.GetBatchAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, ShikiMetadata>());

        var mockUserRepo = new Mock<IUserAnimeRepository>();
        var mockHttpCache = new Mock<IHttpCacheRepository>();
        var mockHttpFactory = new Mock<IHttpClientFactory>();
        mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var service = new ShikiMetadataService(
            mockHttpFactory.Object,
            mockSettings.Object,
            mockMetaRepo.Object,
            mockUserRepo.Object,
            mockHttpCache.Object,
            new ShikiHostResolver(),
            new SyncUiDispatcher(),
            new ShikiRateLimiter());

        var semField = typeof(ShikiMetadataService).GetField("_concurrentFetches", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var semaphore = (SemaphoreSlim)semField.GetValue(service)!;

        // Exhaust all 2 permits so WaitAsync inside LocalizeItemsAsync must wait and observe cancellation
        await semaphore.WaitAsync();
        await semaphore.WaitAsync();
        Assert.Equal(0, semaphore.CurrentCount);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var items = new List<AnimeEntity>
        {
            new() { Id = 1, MediaKind = MediaKind.Anime, Title = "One" },
            new() { Id = 2, MediaKind = MediaKind.Anime, Title = "Two" }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LocalizeItemsAsync(items, ct: cts.Token));

        // Semaphore must still have 0 available permits (unacquired permits were NOT erroneously released!)
        Assert.Equal(0, semaphore.CurrentCount);
        semaphore.Release(2);
        Assert.Equal(2, semaphore.CurrentCount);
    }

    [Fact]
    public async Task ImageCacheCleanup_PreservesHistoryPostersPassedAsRemoteUrls()
    {
        var tempCacheDir = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", "img_cleanup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempCacheDir);

        try
        {
            const string historyPosterUrl = "https://cdn.myanimelist.net/images/anime/1015/138006l.jpg";
            var hashedFileName = ImageDownloader.GetFileNameForUrl(historyPosterUrl);
            var activeHistoryFile = Path.Combine(tempCacheDir, hashedFileName);
            var staleOrphanFile = Path.Combine(tempCacheDir, "deadbeef_orphan.jpg");

            await File.WriteAllBytesAsync(activeHistoryFile, [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(staleOrphanFile, [5, 6, 7, 8]);

            // Backdate both files past the 7-day threshold
            var oldDate = DateTime.UtcNow.AddDays(-14);
            File.SetLastWriteTimeUtc(activeHistoryFile, oldDate);
            File.SetLastWriteTimeUtc(staleOrphanFile, oldDate);

            var cleanup = new ImageCacheCleanup(tempCacheDir);
            await cleanup.PerformSmartCleanupAsync([historyPosterUrl]);

            Assert.True(File.Exists(activeHistoryFile));
            Assert.False(File.Exists(staleOrphanFile));
        }
        finally
        {
            try { Directory.Delete(tempCacheDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task AnimeCountryService_IgnoresNonAnimeMediaKinds_PreventingMalIdCollision()
    {
        var mockCountryRepo = new Mock<IAnimeCountryRepository>();
        mockCountryRepo.Setup(r => r.GetBatchAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, string>());

        Dictionary<int, string>? persisted = null;
        mockCountryRepo.Setup(r => r.UpsertBatchAsync(It.IsAny<IReadOnlyDictionary<int, string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyDictionary<int, string>, CancellationToken>((dict, _) => persisted = new Dictionary<int, string>(dict))
            .Returns(Task.CompletedTask);

        var service = new AnimeCountryService(mockCountryRepo.Object);

        var manhwa = new AnimeEntity
        {
            Id = 21, // Same numeric ID as One Piece anime!
            MediaKind = MediaKind.Manga,
            Title = "Solo Leveling",
            CountryOfOrigin = "KR"
        };

        bool changed = await service.HydrateCountriesAsync([manhwa]);

        Assert.False(changed);
        Assert.Null(persisted);
        mockCountryRepo.Verify(r => r.UpsertBatchAsync(It.IsAny<IReadOnlyDictionary<int, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
