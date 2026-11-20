using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Data.Repository;
using Kiriha.Services.AppLifecycle;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed partial class ScrobbleSyncRestartTests
{
    [Theory]
    [InlineData(MediaKind.Anime, 7, 8)]
    [InlineData(MediaKind.Anime, 9, 8)]
    [InlineData(MediaKind.Anime, 1, 0)]
    [InlineData(MediaKind.Manga, 9, 8)]
    public async Task ManualProgress_QueueInsertFails_RollsBackAndCanBeRetried(
        MediaKind kind, int previousProgress, int requestedProgress)
    {
        var firstFactory = CreateFactory();
        await using (var setup = firstFactory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync();
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fail_manual_sync_task BEFORE INSERT ON sync_tasks
                BEGIN SELECT RAISE(ABORT, 'Test failure writing the manual progress task'); END;
                """);
        }

        var item = new AnimeEntity
        {
            Id = AnimeId,
            MediaKind = kind,
            Title = "Hagane no Renkinjutsushi",
            Status = UserAnimeStatus.Watching,
            Progress = kind == MediaKind.Anime ? previousProgress : 0,
            ChaptersRead = kind == MediaKind.Anime ? 0 : previousProgress
        };
        var library = new UserAnimeRepository(firstFactory);
        await library.UpsertAsync(item);
        var queue = new SyncTaskRepository(firstFactory);
        var history = new HistoryService(new HistoryRepository(firstFactory));
        var publishedHistory = new List<HistoryItem>();
        history.EntryAdded += publishedHistory.Add;
        var eventBus = new CoreEventBus();
        var publishedProgress = new List<AnimeProgressChangedMessage>();
        using var subscription = eventBus.Subscribe<AnimeProgressChangedMessage>(publishedProgress.Add);

        var tracker = new Mock<ITrackerService>();
        tracker.SetupGet(t => t.Name).Returns(Shikimori);
        tracker.SetupGet(t => t.IsEnabled).Returns(true);
        tracker.Setup(t => t.UpdateProgressAsync(
                AnimeId, requestedProgress, UserAnimeStatus.Watching, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.Success);
        tracker.Setup(t => t.SaveFullListStatusAsync(It.IsAny<AnimeEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.Success);

        var reopenedFactory = CreateFactory();
        var reopenedLibrary = new UserAnimeRepository(reopenedFactory);
        var reopenedQueue = new SyncTaskRepository(reopenedFactory);
        var reopenedHistory = new HistoryService(new HistoryRepository(reopenedFactory));
        bool decrement = previousProgress > requestedProgress;

        using (var background = new BackgroundTaskSupervisor())
        {
            var sync = CreateSyncManager(firstFactory, queue, history, background, tracker.Object);
            using var progress = new AnimeProgressService(
                Mock.Of<IAnimeRepository>(), library, sync, history, new ImmediateProgressDispatcher(), eventBus: eventBus);
            Task SaveAsync() => decrement
                ? progress.SmartDecrementProgressAsync(item)
                : progress.SmartIncrementProgressAsync(item, requestedProgress);

            try
            {
                await Assert.ThrowsAsync<DbUpdateException>(SaveAsync);

                Assert.Equal(previousProgress, ReadProgress(item));
                Assert.Equal(previousProgress, ReadProgress(Assert.Single(await reopenedLibrary.GetAllAsync())));
                Assert.Empty(await reopenedQueue.GetPendingAsync());
                Assert.Empty(await reopenedHistory.GetHistoryAsync());
                Assert.Empty(publishedHistory);
                Assert.Empty(publishedProgress);

                await using (var repair = reopenedFactory.CreateDbContext())
                {
                    await repair.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_manual_sync_task");
                }

                await SaveAsync();

                Assert.Equal(requestedProgress, ReadProgress(item));
                Assert.Equal(requestedProgress, ReadProgress(Assert.Single(await reopenedLibrary.GetAllAsync())));
                var task = Assert.Single(await reopenedQueue.GetPendingAsync());
                Assert.Equal(requestedProgress, task.Progress);
                Assert.Equal(kind, task.MediaKind);
                var entry = Assert.Single(await reopenedHistory.GetHistoryAsync());
                Assert.Equal(requestedProgress, entry.Episode);
                Assert.Equal((int)(decrement ? HistoryActionType.Reverted : HistoryActionType.Watched), entry.ActionType);
                Assert.Equal(TrackerSyncState.Pending, entry.TrackerStatuses[Shikimori].State);
                Assert.Equal(entry.Id, Assert.Single(publishedHistory).Id);
                Assert.Equal(kind == MediaKind.Anime ? 1 : 0, publishedProgress.Count);
            }
            finally
            {
                await sync.StopAsync(CancellationToken.None);
            }
        }

        await DrainRestoredQueueAsync(reopenedFactory, reopenedQueue, reopenedHistory, tracker.Object);
        Assert.Equal(TrackerSyncState.Success, Assert.Single(await reopenedHistory.GetHistoryAsync()).TrackerStatuses[Shikimori].State);
        if (kind == MediaKind.Anime)
        {
            tracker.Verify(t => t.UpdateProgressAsync(
                AnimeId, requestedProgress, UserAnimeStatus.Watching, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
        }
        else
        {
            tracker.Verify(t => t.SaveFullListStatusAsync(
                It.Is<AnimeEntity>(saved => saved.MediaKind == kind && saved.ChaptersRead == requestedProgress),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task ManualDecrements_WaitForEarlierCommit_BeforeReadingProgress()
    {
        var item = new AnimeEntity
        {
            Id = AnimeId,
            Title = "Hagane no Renkinjutsushi",
            Status = UserAnimeStatus.Watching,
            Progress = 9
        };
        var firstCommit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var committedEpisodes = new List<int>();
        var sync = new Mock<ISyncManager>();
        sync.Setup(s => s.CommitProgressChangeAsync(
                It.IsAny<AnimeEntity>(), null, It.IsAny<HistoryItem>(), false, It.IsAny<CancellationToken>()))
            .Returns((AnimeEntity _, UserAnimeStatus? _, HistoryItem entry, bool _, CancellationToken _) =>
            {
                committedEpisodes.Add(entry.Episode);
                return entry.Episode == 8 ? firstCommit.Task : Task.FromResult(true);
            });
        using var progress = new AnimeProgressService(
            Mock.Of<IAnimeRepository>(), Mock.Of<IUserAnimeRepository>(), sync.Object,
            Mock.Of<IHistoryService>(), new ImmediateProgressDispatcher(), eventBus: new CoreEventBus());

        var first = progress.SmartDecrementProgressAsync(item);
        var second = progress.SmartDecrementProgressAsync(item);
        var beforeCommit = (item.Progress, Episodes: committedEpisodes.ToArray());
        firstCommit.SetResult(true);
        await Task.WhenAll(first, second).WaitAsync(Timeout);

        Assert.Equal(9, beforeCommit.Progress);
        Assert.Equal(new[] { 8 }, beforeCommit.Episodes);
        Assert.Equal(new[] { 8, 7 }, committedEpisodes);
        Assert.Equal(7, item.Progress);
    }

    private static int ReadProgress(AnimeEntity item) =>
        item.MediaKind == MediaKind.Anime ? item.Progress : item.ChaptersRead;

    private sealed class ImmediateProgressDispatcher : IUiDispatcher
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
}
