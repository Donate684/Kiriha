using Kiriha.Core.Domain.Collections;
using System.Reflection;
using Kiriha.Core;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Streaming.Services;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Mpv.UI.Services.Player;
using Kiriha.Services.AppLifecycle;
using Kiriha.Data.Core;
using Kiriha.Core.Tracking.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Kiriha.Tests;

public sealed class CoreArchitectureImprovementsTests
{
    [Fact]
    public void AnimeCollectionProjection_CompositeMediaKey_DoesNotCollideBetweenAnimeAndMangaWithSameId()
    {
        using var projection = new AnimeCollectionProjection();

        var anime = new AnimeEntity
        {
            Id = 1,
            MediaKind = MediaKind.Anime,
            Title = "Cowboy Bebop",
            Status = UserAnimeStatus.Watching
        };

        var manga = new AnimeEntity
        {
            Id = 1,
            MediaKind = MediaKind.Manga,
            Title = "Monster",
            Status = UserAnimeStatus.Watching
        };

        projection.Rebuild([anime, manga]);

        Assert.Equal(1, projection.Count(UserAnimeStatus.Watching, MediaKind.Anime));
        Assert.Equal(1, projection.Count(UserAnimeStatus.Watching, MediaKind.Manga));

        var animeResults = projection.Query(UserAnimeStatus.Watching, null, filterNsfw: false, sortBy: "Title", MediaKind.Anime);
        var mangaResults = projection.Query(UserAnimeStatus.Watching, null, filterNsfw: false, sortBy: "Title", MediaKind.Manga);

        Assert.Single(animeResults);
        Assert.Equal("Cowboy Bebop", animeResults[0].Title);
        Assert.Single(mangaResults);
        Assert.Equal("Monster", mangaResults[0].Title);

        // Mutating anime status must still trigger PropertyChanged (subscription was not lost when manga Id=1 was added)
        anime.Status = UserAnimeStatus.Completed;
        Assert.Equal(0, projection.Count(UserAnimeStatus.Watching, MediaKind.Anime));
        Assert.Equal(1, projection.Count(UserAnimeStatus.Completed, MediaKind.Anime));
        Assert.Equal(1, projection.Count(UserAnimeStatus.Watching, MediaKind.Manga));
    }

    [Fact]
    public void AnimeEntity_PosterSource_MemoizesValidationAndAvoidsRepeatedDiskChecks()
    {
        var originalValidator = AnimeEntity.LocalPosterValidator;
        int validationCalls = 0;
        bool simulatedValid = false;

        try
        {
            AnimeEntity.LocalPosterValidator = _ =>
            {
                validationCalls++;
                return simulatedValid;
            };

            var entity = new AnimeEntity
            {
                Id = 42,
                Title = "Frieren",
                MainPictureUrl = "https://cdn.example/frieren.jpg",
                LocalPosterPath = @"C:\Cache\42.jpg"
            };

            // Read PosterSource 50 times (simulating virtualized scroll bindings)
            for (int i = 0; i < 50; i++)
            {
                Assert.Equal("https://cdn.example/frieren.jpg", entity.PosterSource);
            }
            Assert.Equal(1, validationCalls);

            // Simulate PosterBatchDownloader finishing download to the exact same path
            simulatedValid = true;
            entity.LocalPosterPath = @"C:\Cache\42.jpg";

            for (int i = 0; i < 50; i++)
            {
                Assert.Equal(@"C:\Cache\42.jpg", entity.PosterSource);
            }
            Assert.Equal(2, validationCalls);
        }
        finally
        {
            AnimeEntity.LocalPosterValidator = originalValidator;
        }
    }

    [Fact]
    public async Task SyncManager_UpdateProgress_ForManga_PersistsMediaKindAndCallsUpdateMangaProgressAsync()
    {
        var mockTracker = new Mock<ITrackerService>();
        mockTracker.SetupGet(t => t.Name).Returns("AniList");
        mockTracker.SetupGet(t => t.TrackerId).Returns("anilist");
        mockTracker.SetupGet(t => t.IsEnabled).Returns(true);
        mockTracker
            .Setup(t => t.UpdateMangaProgressAsync(77, 45, null, UserAnimeStatus.Watching, 9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.Success);

        SyncTaskEntity? persistedEntity = null;
        var mockRepo = new Mock<ISyncTaskRepository>();
        mockRepo
            .Setup(r => r.AddAsync(It.IsAny<SyncTaskEntity>(), It.IsAny<CancellationToken>()))
            .Callback<SyncTaskEntity, CancellationToken>((e, _) => persistedEntity = e)
            .ReturnsAsync(10);

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

        await syncManager.EnqueueUpdateAsync(77, MediaKind.Manga, 45, UserAnimeStatus.Watching, 9);

        Assert.NotNull(persistedEntity);
        Assert.Equal(MediaKind.Manga, persistedEntity!.MediaKind);

        // Execute queued task via reflection (same pattern as existing SyncManager tests)
        var highQueueField = typeof(SyncManager).GetField("_highPriorityQueue", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var queue = (System.Collections.IEnumerable)highQueueField.GetValue(syncManager)!;
        var queuedTask = queue.Cast<object>().Single();

        var executeMethod = typeof(SyncManager).GetMethod("ExecuteTaskAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var resultTask = (Task<(bool Success, bool Executed)>)executeMethod.Invoke(syncManager, [queuedTask, CancellationToken.None])!;
        var (success, executed) = await resultTask;

        Assert.True(success);
        Assert.True(executed);
        mockTracker.Verify(t => t.UpdateMangaProgressAsync(77, 45, null, UserAnimeStatus.Watching, 9, It.IsAny<CancellationToken>()), Times.Once);
        mockTracker.Verify(t => t.UpdateProgressAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<UserAnimeStatus?>(), It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ShikiStateCache_SharesMappingsAndPostersWhileScopingUserRatesByMirror()
    {
        var cache = new ShikiStateCache();
        cache.MalToShikiMap[(52991, "Anime")] = 52991;
        cache.PosterMemoryCache["animes_52991"] = "https://shikimori.one/posters/52991.jpg";

        cache.SetUserRateId(ShikiMirror.One, "Anime", 52991, 1001);
        cache.SetUserRateId(ShikiMirror.Net, "Anime", 52991, 2002);

        Assert.True(cache.TryGetUserRateId(ShikiMirror.One, "Anime", 52991, out int oneRate));
        Assert.True(cache.TryGetUserRateId(ShikiMirror.Net, "Anime", 52991, out int netRate));
        Assert.Equal(1001, oneRate);
        Assert.Equal(2002, netRate);

        cache.RemoveUserRateId(ShikiMirror.One, "Anime", 52991);
        Assert.False(cache.TryGetUserRateId(ShikiMirror.One, "Anime", 52991, out _));
        Assert.True(cache.TryGetUserRateId(ShikiMirror.Net, "Anime", 52991, out _));
    }

    [Fact]
    public void PlayerMediaMetadataResolver_UsesInMemoryAnimeRepository_AndMpvUiIsDecoupledFromData()
    {
        // 1. Verify Kiriha.Mpv.UI assembly has zero dependency on Kiriha.Data or EntityFrameworkCore
        var mpvUiAssembly = typeof(IPlayerMediaMetadataResolver).Assembly;
        var referencedNames = mpvUiAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("Kiriha.Data", referencedNames);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", referencedNames);

        // 2. Verify in-memory snapshot resolution without any IDbContextFactory
        var snapshot = new List<AnimeEntity>
        {
            new()
            {
                Id = 52991,
                MediaKind = MediaKind.Anime,
                Title = "Sousou no Frieren",
                RussianTitle = "Провожающая в последний путь Фрирен",
                EnglishTitle = "Frieren: Beyond Journey's End",
                StartYear = 2023,
                TotalEpisodes = 28,
                Status = UserAnimeStatus.Watching
            }
        };

        var mockAnimeRepo = new Mock<IAnimeRepository>();
        mockAnimeRepo.Setup(r => r.TryGetSnapshot()).Returns(snapshot);

        var resolver = new PlayerMediaMetadataResolver(mockAnimeRepo.Object);
        var meta = resolver.Resolve(@"C:\Anime\[SubsPlease] Sousou no Frieren - 12 (1080p).mkv");

        Assert.Equal(52991, meta.AnimeId);
        Assert.Equal("Провожающая в последний путь Фрирен", meta.TitleRu);
        Assert.Equal("12", meta.EpisodeText);
    }

    [Fact]
    public async Task LibrarySyncGate_BlocksBackgroundUpdatesDuringFullSync()
    {
        using var gate = new LibrarySyncGate();

        using (var fullScope = await gate.AcquireFullSyncAsync())
        {
            Assert.True(gate.IsFullSyncActive);
            var bgAttempt = await gate.TryAcquireBackgroundUpdateAsync();
            Assert.Null(bgAttempt);
        }

        Assert.False(gate.IsFullSyncActive);
        using var bgScope = await gate.TryAcquireBackgroundUpdateAsync();
        Assert.NotNull(bgScope);
    }

    [Fact]
    public async Task HistoryService_StronglyTypedHistoryActionType_PersistsExactNumericCode()
    {
        HistoryItem? added = null;
        var mockRepo = new Mock<IHistoryRepository>();
        mockRepo
            .Setup(r => r.AddAsync(It.IsAny<HistoryItem>(), It.IsAny<CancellationToken>()))
            .Callback<HistoryItem, CancellationToken>((item, _) => added = item)
            .Returns(Task.CompletedTask);

        IHistoryService service = new HistoryService(mockRepo.Object);
        await service.AddEntryAsync(52991, "Sousou no Frieren", null, 28, HistoryActionType.ScoreSet, "10", "https://poster");

        Assert.NotNull(added);
        Assert.Equal((int)HistoryActionType.ScoreSet, added!.ActionType);
        Assert.Equal("10", added.Detail);
    }

    [Fact]
    public void AppStartupCoordinator_BuildServiceProvider_ValidatesPlayerModeContainerCleanly()
    {
        var provider = AppStartupCoordinator.BuildServiceProvider(isPlayerMode: true);
        try
        {
            Assert.NotNull(provider.GetService<ISettingsService>());
            Assert.NotNull(provider.GetService<ILocalizer>());
            Assert.NotNull(provider.GetService<IPlayerMediaMetadataResolver>());
            Assert.NotNull(provider.GetService<ITorrServerService>());
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void OnlineRelevanceScorer_LivesInCoreStreamingAssembly()
    {
        Assert.Equal("Kiriha.Core.Streaming", typeof(OnlineRelevanceScorer).Assembly.GetName().Name);
    }

    [Fact]
    public async Task SyncTaskMediaKind_Migration_CreatesMediaKindColumnCleanly()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", $"sync_task_mig_{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var initializer = new DatabaseInitializer(new LocalDbContextFactory(options));

        try
        {
            await initializer.InitializeAsync();
            await using var db = new AppDbContext(options);
            db.SyncTasks.Add(new SyncTaskEntity
            {
                AnimeId = 999,
                MediaKind = MediaKind.Manga,
                Type = "UpdateProgress",
                Progress = 12
            });
            await db.SaveChangesAsync();

            var loaded = await db.SyncTasks.AsNoTracking().SingleAsync(x => x.AnimeId == 999);
            Assert.Equal(MediaKind.Manga, loaded.MediaKind);
        }
        finally
        {
            try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
            try { File.Delete(dbPath); } catch { }
            try { File.Delete(dbPath + "-wal"); } catch { }
            try { File.Delete(dbPath + "-shm"); } catch { }
        }
    }

    private sealed class LocalDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
