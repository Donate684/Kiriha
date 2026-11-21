using System.Threading.Channels;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Data.Core;
using Kiriha.Data.Repository;
using Kiriha.Services.AppLifecycle;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed partial class DatabaseInitializerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Queue_AtRapidRetryLimit_RespectsTrackerOutcome(bool restartDuringOutage, bool permanentRefusal)
    {
        var directory = Directory.CreateTempSubdirectory("Kiriha.SyncRetryLimit.");
        try
        {
            var factory = new TestDbContextFactory(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(directory.FullName, "library.db"),
                    Pooling = false
                }.ToString())
                .Options);
            var initializer = new DatabaseInitializer(factory);
            await initializer.InitializeAsync();
            await SeedLibraryAsync(factory);
            var queue = new SyncTaskRepository(factory);
            var pending = Assert.Single(await queue.GetPendingAsync());
            pending.RetryCount = 4;
            await queue.UpdateAsync(pending);

            var history = new HistoryService(new HistoryRepository(factory));
            var tracker = new Mock<ITrackerService>();
            tracker.SetupGet(t => t.Name).Returns("MyAnimeList");
            tracker.SetupGet(t => t.IsEnabled).Returns(true);
            tracker.SetupSequence(t => t.UpdateProgressAsync(
                    pending.AnimeId, 8, UserAnimeStatus.Watching, null, null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(permanentRefusal ? SyncOutcome.PermanentFailure : SyncOutcome.TransientFailure)
                .ReturnsAsync(restartDuringOutage ? SyncOutcome.TransientFailure : SyncOutcome.Success);
            var notifications = new Mock<INotificationService>();
            using var background = new BackgroundTaskSupervisor();
            var clock = new ManualRetryClock();
            var sync = new SyncManager([tracker.Object], queue, initializer, history, background,
                notifications.Object, retryClock: clock);
            using var timeout = new CancellationTokenSource(MigrationTimeout);
            try
            {
                await sync.StartAsync(CancellationToken.None);
                if (!permanentRefusal)
                {
                    var retry = await clock.Timers.Reader.ReadAsync(timeout.Token);
                    await VerifyPendingAsync(retry.Delay);
                    retry.Fire();
                }

                if (restartDuringOutage)
                {
                    var nextRetry = await clock.Timers.Reader.ReadAsync(timeout.Token);
                    await VerifyPendingAsync(nextRetry.Delay);
                }
                else
                {
                    while ((await queue.GetPendingAsync(timeout.Token)).Count != 0)
                        await Task.Delay(20, timeout.Token);
                }
            }
            finally
            {
                await sync.StopAsync(CancellationToken.None);
                await background.StopAsync(timeout.Token);
                await history.FlushAsync(MigrationTimeout);
            }

            if (restartDuringOutage)
                await ReplayPendingTaskAsync(initializer, queue, history, pending.AnimeId);

            Assert.Empty(await queue.GetPendingAsync());
            var entry = Assert.Single(await history.GetHistoryAsync());
            Assert.Equal(permanentRefusal ? TrackerSyncState.Failed : TrackerSyncState.Success,
                entry.TrackerStatuses["MyAnimeList"].State);
            Assert.Equal(TrackerSyncState.Success, entry.TrackerStatuses["Shikimori"].State);
            Assert.Equal(permanentRefusal ? 1 : 2,
                tracker.Invocations.Count(call => call.Method.Name == nameof(ITrackerService.UpdateProgressAsync)));
            notifications.Verify(n => n.NotifySyncFailed(It.IsAny<string>(), "MyAnimeList", It.IsAny<string>(), false),
                permanentRefusal ? Times.Once() : Times.Never());
            Assert.False(clock.Timers.Reader.TryRead(out _));

            async Task VerifyPendingAsync(TimeSpan delay)
            {
                Assert.Equal(TimeSpan.FromMinutes(32), delay);
                var saved = Assert.Single(await queue.GetPendingAsync(timeout.Token));
                Assert.Equal(pending.Id, saved.Id);
                Assert.Equal(5, saved.RetryCount);
                Assert.Equal(pending.SuccessfulTrackersJson, saved.SuccessfulTrackersJson);
                await history.FlushAsync(MigrationTimeout);
                var status = Assert.Single(await history.GetHistoryAsync());
                Assert.Equal(TrackerSyncState.Retrying, status.TrackerStatuses["MyAnimeList"].State);
            }
        }
        finally
        {
            await DeleteMigrationDirectoryAsync(directory);
        }
    }

    private sealed class ManualRetryClock : TimeProvider
    {
        public Channel<(TimeSpan Delay, Action Fire)> Timers { get; } =
            Channel.CreateUnbounded<(TimeSpan Delay, Action Fire)>();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Timers.Writer.TryWrite((dueTime, () => callback(state)));
            return Mock.Of<ITimer>();
        }
    }
}
