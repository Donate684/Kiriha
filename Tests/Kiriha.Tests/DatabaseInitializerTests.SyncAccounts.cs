using System.Threading.Channels;
using Kiriha.Core.Abstractions.Services;
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

public sealed partial class DatabaseInitializerTests
{
    [Fact]
    public async Task Queue_RestartAndAccountSwitch_ReplaysEachMutationOnlyForItsOriginalRecipient()
    {
        using var timeout = new CancellationTokenSource(MigrationTimeout);
        var ct = timeout.Token;
        var directory = Directory.CreateTempSubdirectory("Kiriha.SyncAccounts.");
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
            var queue = new SyncTaskRepository(factory);
            var history = new HistoryService(new HistoryRepository(factory));
            var settings = new Mock<ISettingsService>();
            AppSettings current = new();
            settings.SetupGet(s => s.Current).Returns(() => Volatile.Read(ref current));
            var primary = CreateAccountTracker(TrackerConstants.Ids.Mal, "MyAnimeList");
            var mirror = CreateAccountTracker(TrackerConstants.Ids.ShikiOrig, "Shikimori");
            var delivered = Channel.CreateUnbounded<(string Tracker, string? Token, int Progress)>();
            CaptureWrites(primary, TrackerConstants.Ids.Mal);
            CaptureWrites(mirror, TrackerConstants.Ids.ShikiOrig);
            var progressRepo = new ProgressChangeRepository(factory);
            using var firstBackground = new BackgroundTaskSupervisor();
            var originalSession = new SyncManager([primary.Object, mirror.Object], queue, initializer,
                history, firstBackground, settingsService: settings.Object, progressChangeRepository: progressRepo);

            const int id = 777;
            var legacyId = await queue.AddAsync(new SyncTaskEntity
            {
                AnimeId = id, Type = "UpdateProgress", Progress = 2, RetryCount = 5
            }, ct);
            Volatile.Write(ref current, WithAccounts(
                ConfiguredAccount(TrackerConstants.Ids.Mal, 11, "primary-a", primary: true),
                ConfiguredAccount(TrackerConstants.Ids.ShikiOrig, 11, "mirror-a")));
            var item = new AnimeEntity { Id = id, Title = "Example", Progress = 8, Status = UserAnimeStatus.Watching };
            var entry = new HistoryItem { AnimeId = id, AnimeTitle = item.Title, Episode = 8, Timestamp = DateTime.UtcNow };
            Assert.True(await originalSession.CommitProgressChangeAsync(item, null, entry, onlyAdvance: true, ct));
            await originalSession.EnqueueUpdateAsync(id, MediaKind.Anime, 12, targetTracker: "Unavailable tracker");
            Assert.Equal(3, (await queue.GetPendingAsync(ct)).Count);
            await originalSession.StopAsync(CancellationToken.None);

            await new DatabaseMaintenance(factory).PerformAsync();
            Assert.Equal(3, (await queue.GetPendingAsync(ct)).Count);

            Volatile.Write(ref current, new AppSettings());
            using var background = new BackgroundTaskSupervisor();
            var clock = new ManualRetryClock();
            var restarted = new SyncManager([primary.Object, mirror.Object], queue, initializer,
                history, background, settingsService: settings.Object, retryClock: clock);
            try
            {
                await restarted.StartAsync(ct);
                var firstPark = await clock.Timers.Reader.ReadAsync(ct);
                var secondPark = await clock.Timers.Reader.ReadAsync(ct);
                Assert.False(delivered.Reader.TryRead(out _));

                // A second MAL user edits the same title while the two original tasks wait.
                Volatile.Write(ref current, WithAccounts(ConfiguredAccount(TrackerConstants.Ids.Mal, 22, "primary-b", primary: true)));
                await restarted.EnqueueUpdateAsync(id, MediaKind.Anime, 3);
                Assert.Equal((TrackerConstants.Ids.Mal, "primary-b", 3), await delivered.Reader.ReadAsync(ct));

                Volatile.Write(ref current, WithAccounts(
                    ConfiguredAccount(TrackerConstants.Ids.Mal, 11, "primary-a-reconnected", primary: true),
                    ConfiguredAccount(TrackerConstants.Ids.ShikiOrig, 11, "mirror-a-reconnected")));
                firstPark.Fire();
                secondPark.Fire();
                var replayed = new[] { await delivered.Reader.ReadAsync(ct), await delivered.Reader.ReadAsync(ct) };
                Assert.Contains((TrackerConstants.Ids.Mal, "primary-a-reconnected", 8), replayed);
                Assert.Contains((TrackerConstants.Ids.ShikiOrig, "mirror-a-reconnected", 8), replayed);

                List<SyncTaskEntity> pending;
                while ((pending = await queue.GetPendingAsync(ct)).Count > 1)
                    await Task.Delay(20, ct);
                Assert.Equal(legacyId, Assert.Single(pending).Id);
                Assert.False(delivered.Reader.TryRead(out _));
            }
            finally
            {
                await restarted.StopAsync(CancellationToken.None);
                await background.StopAsync(ct);
                await history.FlushAsync(MigrationTimeout);
            }

            void CaptureWrites(Mock<ITrackerService> tracker, string trackerId)
            {
                tracker.Setup(t => t.UpdateProgressAsync(id, It.IsAny<int>(), It.IsAny<UserAnimeStatus?>(),
                        null, null, null, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((int _, int progress, UserAnimeStatus? _, int? _, bool? _, int? _, CancellationToken _) =>
                    {
                        delivered.Writer.TryWrite((trackerId,
                            TrackerRequestAccount.Pinned(trackerId)?.Tokens?.AccessToken, progress));
                        return SyncOutcome.Success;
                    });
            }
        }
        finally
        {
            await DeleteMigrationDirectoryAsync(directory);
        }
    }

    private static Mock<ITrackerService> CreateAccountTracker(string id, string name)
    {
        var tracker = new Mock<ITrackerService>(MockBehavior.Strict);
        tracker.SetupGet(t => t.TrackerId).Returns(id);
        tracker.SetupGet(t => t.Name).Returns(name);
        return tracker;
    }

    private static TrackerAccount ConfiguredAccount(string trackerId, int userId, string accessToken, bool primary = false) => new()
    {
        TrackerId = trackerId,
        AuthenticatedUserId = userId,
        Tokens = new MalTokens { AccessToken = accessToken },
        IsPrimary = primary,
        IsMirror = !primary
    };

    private static AppSettings WithAccounts(params TrackerAccount[] accounts) => new()
    {
        Api = new AppSettings.ApiConfig { Accounts = [.. accounts] }
    };
}
