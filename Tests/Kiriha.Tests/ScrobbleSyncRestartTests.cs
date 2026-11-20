using System.Text.Json;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Data.Core;
using Kiriha.Data.Repository;
using Kiriha.Infrastructure.Settings;
using Kiriha.Services.AppLifecycle;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed partial class ScrobbleSyncRestartTests : IDisposable
{
    private const int AnimeId = 777;
    private const int Episode = 8;
    private const string Shikimori = "Shikimori";
    private const string Mal = "MyAnimeList";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("Kiriha.SyncRestart.");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scrobble_RestartAfterPartialSync_ResumesOnlyUnconfirmedTracker(bool stopDuringMalRequest)
    {
        var firstFactory = CreateFactory();
        await using (var setup = firstFactory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var anime = new AnimeEntity
        {
            Id = AnimeId,
            Title = "Hagane no Renkinjutsushi",
            Progress = Episode - 1,
            Status = UserAnimeStatus.Watching
        };
        var userAnime = new UserAnimeRepository(firstFactory);
        await userAnime.UpsertAsync(anime);

        var malStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstShiki = CreateTracker(Shikimori, _ => Task.FromResult(SyncOutcome.Success));
        var firstMal = CreateTracker(Mal, async ct =>
        {
            malStarted.SetResult();
            if (stopDuringMalRequest)
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
            return SyncOutcome.TransientFailure;
        });
        var firstQueue = new SyncTaskRepository(firstFactory);
        var firstHistory = new HistoryService(new HistoryRepository(firstFactory));

        using (var background = new BackgroundTaskSupervisor())
        {
            var sync = CreateSyncManager(firstFactory, firstQueue, firstHistory, background, firstShiki.Object, firstMal.Object);
            using var scrobbler = CreateScrobbler(userAnime, sync, firstHistory, background);
            await sync.StartAsync(CancellationToken.None);
            try
            {
                scrobbler.StartScrobble(new ParsedMedia { Episode = "8", IsPlaying = true }, anime);
                await malStarted.Task.WaitAsync(Timeout);
                if (!stopDuringMalRequest)
                    await WaitForQueueAsync(firstQueue, tasks => tasks.Any(task => task.RetryCount == 1));
            }
            finally
            {
                await sync.StopAsync(CancellationToken.None);
                using var stopTimeout = new CancellationTokenSource(Timeout);
                await background.StopAsync(stopTimeout.Token);
                await firstHistory.FlushAsync(Timeout);
            }
        }

        var restartedFactory = CreateFactory();
        var restartedQueue = new SyncTaskRepository(restartedFactory);
        var restartedHistory = new HistoryService(new HistoryRepository(restartedFactory));
        var persistedTask = Assert.Single(await restartedQueue.GetPendingAsync());
        Assert.Equal(Episode, persistedTask.Progress);
        Assert.Equal(stopDuringMalRequest ? 0 : 1, persistedTask.RetryCount);
        var resolvedTrackers = JsonSerializer.Deserialize<string[]>(persistedTask.SuccessfulTrackersJson ?? "[]");
        Assert.Equal(new[] { Shikimori }, resolvedTrackers);

        var historyBeforeRetry = Assert.Single(await restartedHistory.GetHistoryAsync());
        Assert.Equal(TrackerSyncState.Success, historyBeforeRetry.TrackerStatuses[Shikimori].State);
        Assert.Equal(stopDuringMalRequest ? TrackerSyncState.Pending : TrackerSyncState.Retrying,
            historyBeforeRetry.TrackerStatuses[Mal].State);

        var restartedShiki = CreateTracker(Shikimori, _ => Task.FromResult(SyncOutcome.Success));
        var restartedMal = CreateTracker(Mal, _ => Task.FromResult(SyncOutcome.Success));
        await DrainRestoredQueueAsync(restartedFactory, restartedQueue, restartedHistory, restartedShiki.Object, restartedMal.Object);

        Assert.Single(firstShiki.Invocations, call => call.Method.Name == nameof(ITrackerService.UpdateProgressAsync));
        restartedShiki.Verify(tracker => tracker.UpdateProgressAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<UserAnimeStatus?>(), It.IsAny<int?>(),
            It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        restartedMal.Verify(tracker => tracker.UpdateProgressAsync(
            AnimeId, Episode, UserAnimeStatus.Watching, null, null, null, It.IsAny<CancellationToken>()), Times.Once);

        var localAnime = Assert.Single(await new UserAnimeRepository(restartedFactory).GetAllAsync());
        Assert.Equal(Episode, localAnime.Progress);
        var entry = Assert.Single(await restartedHistory.GetHistoryAsync());
        Assert.Equal(historyBeforeRetry.Id, entry.Id);
        Assert.Equal(Episode, entry.Episode);
        Assert.Equal((int)HistoryActionType.Scrobbled, entry.ActionType);
        Assert.Equal(TrackerSyncState.Success, entry.TrackerStatuses[Shikimori].State);
        Assert.Equal(TrackerSyncState.Success, entry.TrackerStatuses[Mal].State);
    }

    [Theory]
    [InlineData(1, 2, false)]
    [InlineData(9, 8, false)]
    [InlineData(1, 2, true)]
    public async Task RestartWithOfflineEdits_PreservesLatestProgressAndFullUpdateFields(
        int earlierProgress, int latestProgress, bool fullUpdateIsNewer)
    {
        var firstFactory = CreateFactory();
        await using (var setup = firstFactory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync();
        }

        const string notes = "Continue with the next season";
        var fullItem = new AnimeEntity
        {
            Id = AnimeId,
            Title = "Hagane no Renkinjutsushi",
            Progress = fullUpdateIsNewer ? latestProgress : earlierProgress,
            Status = UserAnimeStatus.Watching,
            Notes = notes
        };
        var firstQueue = new SyncTaskRepository(firstFactory);
        var firstHistory = new HistoryService(new HistoryRepository(firstFactory));
        using (var background = new BackgroundTaskSupervisor())
        {
            // Persist both edits before any tracker can receive them.
            var offlineSync = CreateSyncManager(firstFactory, firstQueue, firstHistory, background);
            try
            {
                if (fullUpdateIsNewer)
                {
                    await offlineSync.EnqueueUpdateAsync(AnimeId, MediaKind.Anime, earlierProgress);
                    await offlineSync.EnqueueFullUpdateAsync(fullItem);
                }
                else
                {
                    await offlineSync.EnqueueFullUpdateAsync(fullItem);
                    await offlineSync.EnqueueUpdateAsync(AnimeId, MediaKind.Anime, latestProgress);
                }
            }
            finally
            {
                await offlineSync.StopAsync(CancellationToken.None);
            }
        }

        var tracker = new Mock<ITrackerService>();
        tracker.SetupGet(t => t.Name).Returns(Shikimori);
        tracker.SetupGet(t => t.IsEnabled).Returns(true);
        var sentProgress = new List<int>();
        tracker.Setup(t => t.SaveFullListStatusAsync(It.IsAny<AnimeEntity>(), It.IsAny<CancellationToken>()))
            .Callback((AnimeEntity item, CancellationToken _) => sentProgress.Add(item.Progress))
            .ReturnsAsync(SyncOutcome.Success);
        tracker.Setup(t => t.UpdateProgressAsync(
                AnimeId, It.IsAny<int>(), null, null, null, null, It.IsAny<CancellationToken>()))
            .Callback((int _, int progress, UserAnimeStatus? _, int? _, bool? _, int? _, CancellationToken _) =>
                sentProgress.Add(progress))
            .ReturnsAsync(SyncOutcome.Success);

        var restartedFactory = CreateFactory();
        var restartedQueue = new SyncTaskRepository(restartedFactory);
        var restartedHistory = new HistoryService(new HistoryRepository(restartedFactory));
        await DrainRestoredQueueAsync(restartedFactory, restartedQueue, restartedHistory, tracker.Object);

        Assert.NotEmpty(sentProgress);
        Assert.All(sentProgress, progress => Assert.Equal(latestProgress, progress));
        tracker.Verify(t => t.SaveFullListStatusAsync(
            It.Is<AnimeEntity>(item => item.Id == AnimeId && item.Status == UserAnimeStatus.Watching && item.Notes == notes),
            It.IsAny<CancellationToken>()), Times.Once);
        if (fullUpdateIsNewer)
        {
            tracker.Verify(t => t.UpdateProgressAsync(
                AnimeId, It.IsAny<int>(), It.IsAny<UserAnimeStatus?>(), It.IsAny<int?>(),
                It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scrobble_QueueInsertFails_RollsBackProgressAndHistory_AndCanBeRetried(bool finalEpisode)
    {
        var firstFactory = CreateFactory();
        await using (var setup = firstFactory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync();
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fail_sync_task BEFORE INSERT ON sync_tasks
                BEGIN SELECT RAISE(ABORT, 'Test failure writing the sync task'); END;
                """);
        }

        var anime = new AnimeEntity
        {
            Id = AnimeId,
            Title = "Hagane no Renkinjutsushi",
            Progress = Episode - 1,
            TotalEpisodes = finalEpisode ? Episode : Episode + 1,
            Status = UserAnimeStatus.Watching
        };
        var staleMatch = anime.Clone();
        var userAnime = new UserAnimeRepository(firstFactory);
        await userAnime.UpsertAsync(anime);
        var queue = new SyncTaskRepository(firstFactory);
        var history = new HistoryService(new HistoryRepository(firstFactory));
        var published = new List<HistoryItem>();
        history.EntryAdded += published.Add;
        var tracker = CreateTracker(Shikimori, _ => Task.FromResult(SyncOutcome.Success));
        tracker.Setup(t => t.SaveFullListStatusAsync(It.IsAny<AnimeEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.Success);

        Task? countdown = null;
        var background = new Mock<IBackgroundTaskSupervisor>();
        background.Setup(x => x.Run("ScrobbleService.Countdown", It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Func<CancellationToken, Task> work, CancellationToken ct) => countdown = work(ct));

        var offlineSync = CreateSyncManager(firstFactory, queue, history, background.Object, tracker.Object);
        using var scrobbler = CreateScrobbler(userAnime, offlineSync, history, background.Object);
        var media = new ParsedMedia { Episode = "8", IsPlaying = true };
        var reopenedFactory = CreateFactory();
        var reopenedLibrary = new UserAnimeRepository(reopenedFactory);
        var reopenedQueue = new SyncTaskRepository(reopenedFactory);
        var reopenedHistory = new HistoryService(new HistoryRepository(reopenedFactory));

        try
        {
            await RunScrobbleAsync(anime);

            Assert.Equal(Episode - 1, anime.Progress);
            Assert.Equal(UserAnimeStatus.Watching, anime.Status);
            var beforeRetry = Assert.Single(await reopenedLibrary.GetAllAsync());
            Assert.Equal(Episode - 1, beforeRetry.Progress);
            Assert.Equal(UserAnimeStatus.Watching, beforeRetry.Status);
            Assert.Null(beforeRetry.DateCompleted);
            Assert.Empty(await reopenedQueue.GetPendingAsync());
            Assert.Empty(await reopenedHistory.GetHistoryAsync());
            Assert.Empty(published);

            await using (var repair = reopenedFactory.CreateDbContext())
            {
                await repair.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_sync_task");
            }
            await RunScrobbleAsync(anime);
            await RunScrobbleAsync(staleMatch);

            Assert.Equal(Episode, anime.Progress);
            var saved = Assert.Single(await reopenedLibrary.GetAllAsync());
            Assert.Equal(Episode, saved.Progress);
            Assert.Equal(finalEpisode ? UserAnimeStatus.Completed : UserAnimeStatus.Watching, saved.Status);
            Assert.Equal(finalEpisode, saved.DateCompleted.HasValue);
            Assert.Equal(Episode, Assert.Single(await reopenedQueue.GetPendingAsync()).Progress);
            var entry = Assert.Single(await reopenedHistory.GetHistoryAsync());
            Assert.Equal((int)(finalEpisode ? HistoryActionType.Completed : HistoryActionType.Scrobbled), entry.ActionType);
            Assert.Equal(TrackerSyncState.Pending, entry.TrackerStatuses[Shikimori].State);
            Assert.Equal(entry.Id, Assert.Single(published).Id);
        }
        finally
        {
            await offlineSync.StopAsync(CancellationToken.None);
        }

        await DrainRestoredQueueAsync(reopenedFactory, reopenedQueue, reopenedHistory, tracker.Object);
        Assert.Equal(TrackerSyncState.Success, Assert.Single(await reopenedHistory.GetHistoryAsync()).TrackerStatuses[Shikimori].State);
        if (finalEpisode)
        {
            tracker.Verify(t => t.SaveFullListStatusAsync(
                It.Is<AnimeEntity>(item => item.Progress == Episode && item.Status == UserAnimeStatus.Completed),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        else
        {
            tracker.Verify(t => t.UpdateProgressAsync(
                AnimeId, Episode, UserAnimeStatus.Watching, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        async Task RunScrobbleAsync(AnimeEntity match)
        {
            countdown = null;
            scrobbler.StartScrobble(media, match);
            Assert.NotNull(countdown);
            await countdown.WaitAsync(Timeout);
        }
    }

    private static async Task DrainRestoredQueueAsync(
        FileDbContextFactory factory, SyncTaskRepository queue, HistoryService history, params ITrackerService[] trackers)
    {
        using var background = new BackgroundTaskSupervisor();
        var sync = CreateSyncManager(factory, queue, history, background, trackers);
        await sync.StartAsync(CancellationToken.None);
        try
        {
            await WaitForQueueAsync(queue, tasks => tasks.Count == 0);
        }
        finally
        {
            await sync.StopAsync(CancellationToken.None);
            await history.FlushAsync(Timeout);
        }
    }

    private FileDbContextFactory CreateFactory()
    {
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_directory.FullName, "kiriha.db"),
            Pooling = false
        };
        return new FileDbContextFactory(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection.ToString())
            .Options);
    }

    private ScrobbleService CreateScrobbler(
        UserAnimeRepository userAnime, SyncManager sync, HistoryService history, IBackgroundTaskSupervisor background)
    {
        var settings = new SettingsService(Path.Combine(_directory.FullName, "test-settings.json"));
        settings.Update(s =>
        {
            s.System.Scrobbler.Enabled = true;
            s.System.Scrobbler.Mode = ScrobbleDelayMode.Time;
            s.System.Scrobbler.DelaySeconds = 0;
        }, save: false);
        var dispatcher = new Mock<IUiDispatcher>();
        dispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<bool>>())).Returns((Func<bool> action) => Task.FromResult(action()));
        dispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<AnimeEntity?>>()))
            .Returns((Func<AnimeEntity?> action) => Task.FromResult(action()));
        dispatcher.Setup(d => d.InvokeAsync(It.IsAny<Action>())).Returns((Action action) =>
        {
            action();
            return Task.CompletedTask;
        });
        var eventBus = new CoreEventBus();
        var progress = new AnimeProgressService(
            Mock.Of<IAnimeRepository>(), userAnime, sync, history, dispatcher.Object, eventBus: eventBus);
        return new ScrobbleService(progress, settings, Mock.Of<INotificationService>(),
            background, dispatcher.Object, ILocalizer.Passthrough, eventBus: eventBus);
    }

    private static SyncManager CreateSyncManager(
        FileDbContextFactory factory, SyncTaskRepository queue, HistoryService history, IBackgroundTaskSupervisor background,
        params ITrackerService[] trackers)
    {
        var initializer = new Mock<IDatabaseInitializer>();
        initializer.SetupGet(db => db.InitializationTask).Returns(Task.CompletedTask);
        return new SyncManager(trackers, queue, initializer.Object, history, background,
            progressChangeRepository: new ProgressChangeRepository(factory));
    }

    private static Mock<ITrackerService> CreateTracker(string name, Func<CancellationToken, Task<SyncOutcome>> update)
    {
        var tracker = new Mock<ITrackerService>();
        tracker.SetupGet(t => t.Name).Returns(name);
        tracker.SetupGet(t => t.IsEnabled).Returns(true);
        tracker.Setup(t => t.UpdateProgressAsync(
                AnimeId, Episode, UserAnimeStatus.Watching, null, null, null, It.IsAny<CancellationToken>()))
            .Returns((int _, int _, UserAnimeStatus? _, int? _, bool? _, int? _, CancellationToken ct) => update(ct));
        return tracker;
    }

    private static async Task WaitForQueueAsync(
        SyncTaskRepository queue, Func<List<SyncTaskEntity>, bool> reachedState)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!reachedState(await queue.GetPendingAsync(timeout.Token)))
            await Task.Delay(20, timeout.Token);
    }

    public void Dispose() => _directory.Delete(recursive: true);

    private sealed class FileDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
