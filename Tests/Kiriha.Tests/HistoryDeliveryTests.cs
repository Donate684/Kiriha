using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Auth;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Sync;
using Kiriha.Data.Core;
using Kiriha.Data.Repository;
using Kiriha.Services.AppLifecycle;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed class HistoryDeliveryTests : IDisposable
{
    private const int AnimeId = 777;
    private const string TrackerName = "MyAnimeList";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("Kiriha.HistoryDelivery.");

    [Fact]
    public async Task OldHistoryWithTransientFailure_SurvivesMaintenanceAndReceivesRestartedDelivery()
    {
        var fixture = await CreateFixtureAsync();
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        using var background = new BackgroundTaskSupervisor();
        var original = NewHistory();
        original.Timestamp = original.Timestamp.AddDays(-200);
        fixture.Tracker.Setup(t => t.UpdateProgressAsync(AnimeId, 8, UserAnimeStatus.Watching,
            null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(SyncOutcome.TransientFailure);
        var manager = fixture.CreateManager(background);
        try
        {
            await manager.StartAsync(ct);
            Assert.True(await manager.CommitProgressChangeAsync(NewItem(), null, original, false, ct));
            while (!(await fixture.Queue.GetPendingAsync(ct)).Any(task => task.RetryCount > 0))
                await Task.Delay(25, ct);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }

        Assert.True(await fixture.PerformMaintenanceAsync(ct));
        Assert.Equal(original.Id, Assert.Single(await fixture.HistoryStore.GetAsync(ct: ct)).Id);
        fixture.Tracker.Setup(t => t.UpdateProgressAsync(AnimeId, 8, UserAnimeStatus.Watching,
            null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(SyncOutcome.Success);
        var restarted = fixture.CreateManager(background);
        try
        {
            await restarted.StartAsync(ct);
            await WaitUntilDrainedAsync(fixture.Queue, ct);
            var delivered = Assert.Single(await fixture.HistoryStore.GetAsync(ct: ct));
            Assert.Equal(original.Id, delivered.Id);
            Assert.Equal(TrackerSyncState.Success, delivered.TrackerStatuses[TrackerName].State);
            Assert.True(await fixture.PerformMaintenanceAsync(ct));
            Assert.Empty(await fixture.HistoryStore.GetAsync(ct: ct));
        }
        finally
        {
            await restarted.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task LateReplyAfterAccountSwitch_UpdatesOnlyTheOriginalHistoryRow()
    {
        var fixture = await CreateFixtureAsync();
        using var timeout = new CancellationTokenSource(Timeout);
        using var background = new BackgroundTaskSupervisor();
        var firstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResponse = new TaskCompletionSource<SyncOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Tracker.Setup(t => t.UpdateProgressAsync(
                AnimeId, 8, UserAnimeStatus.Watching, null, null, null, It.IsAny<CancellationToken>()))
            .Returns((int _, int _, UserAnimeStatus? _, int? _, bool? _, int? _, CancellationToken ct) =>
            {
                if (TrackerRequestAccount.Pinned(TrackerConstants.Ids.Mal)?.Tokens?.AccessToken == "account-a")
                {
                    firstRequest.TrySetResult();
                    return firstResponse.Task.WaitAsync(ct);
                }

                return Task.FromResult(SyncOutcome.PermanentFailure);
            });
        var manager = fixture.CreateManager(background);
        try
        {
            await manager.StartAsync(timeout.Token);
            var original = NewHistory();
            Assert.True(await manager.CommitProgressChangeAsync(NewItem(), null, original, false, timeout.Token));
            await firstRequest.Task.WaitAsync(timeout.Token);

            fixture.SignIn(22, "account-b");
            var other = NewHistory();
            Assert.True(await manager.CommitProgressChangeAsync(NewItem(), null, other, false, timeout.Token));
            firstResponse.SetResult(SyncOutcome.Success);
            await WaitUntilDrainedAsync(fixture.Queue, timeout.Token);

            var history = await fixture.HistoryStore.GetAsync(ct: timeout.Token);
            Assert.Equal(TrackerSyncState.Success, history.Single(h => h.Id == original.Id).TrackerStatuses[TrackerName].State);
            Assert.Equal(TrackerSyncState.Failed, history.Single(h => h.Id == other.Id).TrackerStatuses[TrackerName].State);
            fixture.SignIn(11, "account-a-new");
            Assert.Equal(HistoryRetryResult.AccountUnavailable,
                await manager.RetryHistoryDeliveryAsync(other.Id, TrackerName, timeout.Token));
            Assert.Empty(await fixture.Queue.GetPendingAsync(timeout.Token));
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryAfterRestart_UsesOriginalMutationAndOriginalAccount(bool remove)
    {
        var fixture = await CreateFixtureAsync();
        using var timeout = new CancellationTokenSource(Timeout);
        using var background = new BackgroundTaskSupervisor();
        fixture.Tracker.Setup(t => t.SaveFullListStatusAsync(It.IsAny<AnimeEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.PermanentFailure);
        fixture.Tracker.Setup(t => t.RemoveAnimeAsync(AnimeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.PermanentFailure);
        var entry = NewHistory(remove ? HistoryActionType.Deleted : HistoryActionType.Edited);
        var manager = fixture.CreateManager(background);
        try
        {
            Assert.True(await manager.CommitEntryChangeAsync(AnimeId, MediaKind.Anime, remove ? null : NewItem(), [entry], timeout.Token));
            await manager.StartAsync(timeout.Token);
            await WaitUntilDrainedAsync(fixture.Queue, timeout.Token);
            var oldStatus = Assert.Single(await fixture.HistoryStore.GetAsync(ct: timeout.Token)).TrackerStatuses[TrackerName];
            Assert.Equal(TrackerSyncState.Failed, oldStatus.State);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }

        fixture.SignIn(22, "another-user");
        var restarted = fixture.CreateManager(background);
        string? recipient = null;
        fixture.Tracker.Setup(t => t.SaveFullListStatusAsync(It.Is<AnimeEntity>(x =>
                x.Id == AnimeId && x.Notes == "Original notes" && x.Progress == 8 && x.UserScore == 7),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Deliver);
        fixture.Tracker.Setup(t => t.RemoveAnimeAsync(AnimeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Deliver);
        try
        {
            Assert.Equal(HistoryRetryResult.AccountUnavailable,
                await restarted.RetryHistoryDeliveryAsync(entry.Id, TrackerName, timeout.Token));
            fixture.SignIn(11, "original-user-with-new-token");
            Assert.Equal(HistoryRetryResult.Queued, await restarted.RetryHistoryDeliveryAsync(entry.Id, TrackerName, timeout.Token));
            Assert.Equal(HistoryRetryResult.NotRetryable, await restarted.RetryHistoryDeliveryAsync(entry.Id, TrackerName, timeout.Token));
            await restarted.StartAsync(timeout.Token);
            await WaitUntilDrainedAsync(fixture.Queue, timeout.Token);
            Assert.Equal("original-user-with-new-token", recipient);
            Assert.Equal(TrackerSyncState.Success, Assert.Single(await fixture.HistoryStore.GetAsync()).TrackerStatuses[TrackerName].State);
            fixture.Tracker.Verify(t => t.RemoveAnimeAsync(AnimeId, It.IsAny<CancellationToken>()), remove ? Times.Exactly(2) : Times.Never());
            fixture.Tracker.Verify(t => t.SaveFullListStatusAsync(It.IsAny<AnimeEntity>(), It.IsAny<CancellationToken>()),
                remove ? Times.Never() : Times.Exactly(2));
        }
        finally
        {
            await restarted.StopAsync(CancellationToken.None);
        }

        SyncOutcome Deliver()
        {
            recipient = TrackerRequestAccount.Pinned(TrackerConstants.Ids.Mal)?.Tokens?.AccessToken;
            return SyncOutcome.Success;
        }
    }

    [Fact]
    public async Task FailedReceiptCannotBeRetriedAfterANewerEditForTheSameAccount()
    {
        var fixture = await CreateFixtureAsync();
        using var timeout = new CancellationTokenSource(Timeout);
        using var background = new BackgroundTaskSupervisor();
        var manager = fixture.CreateManager(background);
        try
        {
            var older = NewHistory();
            Assert.True(await manager.CommitProgressChangeAsync(NewItem(), null, older, false, timeout.Token));
            var failed = Assert.Single(await fixture.Queue.GetPendingAsync(timeout.Token));
            await fixture.Queue.CheckpointDeliveryAsync(failed, TrackerName, TrackerSyncState.Failed, "Refused", timeout.Token);
            await fixture.Queue.RemoveAsync(failed.Id, timeout.Token);

            var newer = NewHistory(HistoryActionType.Edited);
            var changed = NewItem();
            changed.Notes = "Later edit";
            Assert.True(await manager.CommitEntryChangeAsync(AnimeId, MediaKind.Anime, changed, [newer], timeout.Token));
            var pending = Assert.Single(await fixture.Queue.GetPendingAsync(timeout.Token));
            await fixture.Queue.CheckpointDeliveryAsync(pending, TrackerName, TrackerSyncState.Success, null, timeout.Token);
            await fixture.Queue.RemoveAsync(pending.Id, timeout.Token);

            Assert.Equal(HistoryRetryResult.NotRetryable,
                await manager.RetryHistoryDeliveryAsync(older.Id, TrackerName, timeout.Token));
            Assert.Empty(await fixture.Queue.GetPendingAsync(timeout.Token));
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task FailedCheckpointLeavesBothQueueAndOriginalHistoryUnchanged()
    {
        var fixture = await CreateFixtureAsync();
        using var timeout = new CancellationTokenSource(Timeout);
        using var background = new BackgroundTaskSupervisor();
        var manager = fixture.CreateManager(background);
        try
        {
            var receipt = NewHistory();
            Assert.True(await manager.CommitProgressChangeAsync(NewItem(), null, receipt, false, timeout.Token));
            var task = Assert.Single(await fixture.Queue.GetPendingAsync(timeout.Token));
            task.SuccessfulTrackersJson = """["MyAnimeList"]""";
            await using var context = fixture.OpenContext();
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER refuse_tracker_checkpoint BEFORE UPDATE ON sync_tasks
                BEGIN
                    SELECT RAISE(ABORT, 'Deliberate failure during tracker checkpoint');
                END;
                """, timeout.Token);

            await Assert.ThrowsAsync<DbUpdateException>(() =>
                fixture.Queue.CheckpointDeliveryAsync(task, TrackerName, TrackerSyncState.Success, null, timeout.Token));
            Assert.Null(Assert.Single(await fixture.Queue.GetPendingAsync(timeout.Token)).SuccessfulTrackersJson);
            var unchanged = Assert.Single(await fixture.HistoryStore.GetAsync(ct: timeout.Token));
            Assert.Equal(TrackerSyncState.Pending, unchanged.TrackerStatuses[TrackerName].State);

            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER refuse_tracker_checkpoint", timeout.Token);
            var updated = await fixture.Queue.CheckpointDeliveryAsync(task, TrackerName, TrackerSyncState.Success, null, timeout.Token);
            Assert.NotNull(updated);
            Assert.Equal(receipt.Id, updated.Id);
            Assert.Equal(TrackerSyncState.Success, updated.TrackerStatuses[TrackerName].State);
            Assert.Equal(task.SuccessfulTrackersJson, Assert.Single(await fixture.Queue.GetPendingAsync(timeout.Token)).SuccessfulTrackersJson);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartupDeduplication_DoesNotReportAnUnsentHistoryRowAsSuccess()
    {
        var fixture = await CreateFixtureAsync();
        using var timeout = new CancellationTokenSource(Timeout);
        using var background = new BackgroundTaskSupervisor();
        fixture.Tracker.Setup(t => t.UpdateProgressAsync(AnimeId, 8, UserAnimeStatus.Watching, null, null, null,
            It.IsAny<CancellationToken>())).ReturnsAsync(SyncOutcome.Success);
        var manager = fixture.CreateManager(background);
        try
        {
            var older = NewHistory();
            var newer = NewHistory();
            Assert.True(await manager.CommitProgressChangeAsync(NewItem(), null, older, false, timeout.Token));
            Assert.True(await manager.CommitProgressChangeAsync(NewItem(), null, newer, false, timeout.Token));
            await manager.StartAsync(timeout.Token);
            await WaitUntilDrainedAsync(fixture.Queue, timeout.Token);
            var rows = await fixture.HistoryStore.GetAsync(ct: timeout.Token);
            Assert.Equal(TrackerSyncState.Superseded, rows.Single(h => h.Id == older.Id).TrackerStatuses[TrackerName].State);
            Assert.Equal(TrackerSyncState.Success, rows.Single(h => h.Id == newer.Id).TrackerStatuses[TrackerName].State);
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    private async Task<Fixture> CreateFixtureAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_directory.FullName, "history.db"), Pooling = false
            }.ToString()).Options;
        var factory = new Factory(options);
        await using var context = factory.CreateDbContext();
        await context.Database.EnsureCreatedAsync();
        return new Fixture(factory);
    }

    private static AnimeEntity NewItem() => new()
    {
        Id = AnimeId, Title = "An example series", Status = UserAnimeStatus.Watching,
        Progress = 8, UserScore = 7, Notes = "Original notes"
    };

    private static HistoryItem NewHistory(HistoryActionType action = HistoryActionType.Watched) => new()
    {
        AnimeId = AnimeId, AnimeTitle = "An example series", Episode = 8,
        Timestamp = DateTime.UtcNow, ActionType = (int)action
    };

    private static async Task WaitUntilDrainedAsync(SyncTaskRepository queue, CancellationToken ct)
    {
        while ((await queue.GetPendingAsync(ct)).Count != 0)
            await Task.Delay(25, ct);
    }

    public void Dispose() => _directory.Delete(recursive: true);

    private sealed class Fixture
    {
        private readonly Mock<ISettingsService> _settings = new();
        private readonly IDatabaseInitializer _initializer = Mock.Of<IDatabaseInitializer>(db => db.InitializationTask == Task.CompletedTask);
        private readonly IDbContextFactory<AppDbContext> _factory;
        private AppSettings _current = new();

        public SyncTaskRepository Queue { get; }
        public Task<bool> PerformMaintenanceAsync(CancellationToken ct) => new DatabaseMaintenance(_factory).PerformAsync(ct);
        public HistoryRepository HistoryStore { get; }
        public Mock<ITrackerService> Tracker { get; } = new();

        public Fixture(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
            Queue = new SyncTaskRepository(factory);
            HistoryStore = new HistoryRepository(factory);
            Tracker.SetupGet(t => t.TrackerId).Returns(TrackerConstants.Ids.Mal);
            Tracker.SetupGet(t => t.Name).Returns(TrackerName);
            _settings.SetupGet(s => s.Current).Returns(() => Volatile.Read(ref _current));
            SignIn(11, "account-a");
        }

        public void SignIn(int userId, string token) => Volatile.Write(ref _current, new AppSettings
        {
            Api = new AppSettings.ApiConfig { Accounts = [new TrackerAccount
            {
                TrackerId = TrackerConstants.Ids.Mal, AuthenticatedUserId = userId, IsPrimary = true,
                Tokens = new MalTokens { AccessToken = token }
            }] }
        });

        public SyncManager CreateManager(IBackgroundTaskSupervisor background) =>
            new([Tracker.Object], Queue, _initializer, new HistoryService(HistoryStore), background,
                settingsService: _settings.Object, progressChangeRepository: new ProgressChangeRepository(_factory));

        public AppDbContext OpenContext() => _factory.CreateDbContext();
    }

    private sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
