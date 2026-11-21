using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Data.Core;
using Kiriha.Data.Repository;
using Kiriha.Services;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.AppLifecycle.Shutdown;
using Kiriha.Services.Maintenance;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed partial class ScrobbleSyncRestartTests
{
    private static readonly DateTimeOffset MaintenanceNow = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqliteMaintenance_KeepsHistoryForPendingDeliveriesAndPrunesItAfterAcknowledgement(bool manual)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        var factory = CreateFactory();
        var linked = new HistoryItem { AnimeId = AnimeId, Timestamp = MaintenanceNow.UtcDateTime.AddDays(-200) };
        var stale = new HistoryItem { AnimeId = AnimeId, Timestamp = linked.Timestamp };
        var recent = new HistoryItem { AnimeId = AnimeId, Timestamp = MaintenanceNow.UtcDateTime };
        var owner = new SyncTaskOwner("shikimori", "user:77");
        var first = new SyncTaskEntity
        {
            AnimeId = AnimeId, Type = "UpdateProgress", RetryCount = 40,
            Payload = JsonSerializer.Serialize(new OwnedSyncTaskPayload(owner, null))
        };

        await using (var setup = factory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync(ct);
            setup.History.AddRange(linked, stale, recent);
            await setup.SaveChangesAsync(ct);
            linked.SetTrackerStatus("Shikimori", TrackerSyncState.Pending, originalTask: first);
            first.Payload = JsonSerializer.Serialize(new OwnedSyncTaskPayload(owner, null, HistoryId: linked.Id));
            setup.SyncTasks.AddRange(first,
                new SyncTaskEntity { AnimeId = AnimeId, Payload = "{malformed" },
                new SyncTaskEntity { AnimeId = AnimeId, Payload = """{"HistoryId":"invalid"}""" },
                new SyncTaskEntity { AnimeId = AnimeId });
            await setup.SaveChangesAsync(ct);
        }

        var maintenance = new DatabaseMaintenance(factory, new MaintenanceClock(MaintenanceNow));
        var history = Mock.Of<IHistoryService>();
        var cleanup = new CacheCleanupService(factory, history);
        var queue = new SyncTaskRepository(factory);
        await CleanHistoryAsync();
        var stats = await cleanup.GetStatsAsync(ct);
        Assert.Equal(manual ? 1 : 2, Assert.Single(stats, item => item.Target == CacheCleanupTarget.History).ItemCount);

        await using (var verify = factory.CreateDbContext())
        {
            var ids = await verify.History.OrderBy(item => item.Id).Select(item => item.Id).ToListAsync(ct);
            Assert.Equal(manual ? [linked.Id] : new[] { linked.Id, recent.Id }, ids);
            Assert.Equal(4, await verify.SyncTasks.CountAsync(ct));
        }

        var confirmed = await queue.CheckpointDeliveryAsync(first, "Shikimori", TrackerSyncState.Success, null, ct);
        Assert.NotNull(confirmed);
        Assert.Equal(TrackerSyncState.Success, confirmed.TrackerStatuses["Shikimori"].State);
        await queue.RemoveAsync(first.Id, ct);
        await CleanHistoryAsync();
        await using var final = factory.CreateDbContext();
        Assert.False(await final.History.AnyAsync(item => item.Id == linked.Id, ct));
        Assert.Equal(3, await final.SyncTasks.CountAsync(ct));

        async Task CleanHistoryAsync()
        {
            if (manual)
                await cleanup.ClearAsync([CacheCleanupTarget.History], ct);
            else
                Assert.True(await maintenance.PerformAsync(ct));
        }
    }

    [Fact]
    public async Task SqliteMaintenance_PrunesOnlyExpiredCachesAndOrphanedMetadata()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        var factory = CreateFactory();
        var today = MaintenanceNow.UtcDateTime.Date;
        await using (var setup = factory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync(ct);
            setup.UserAnime.Add(new AnimeEntity { Id = AnimeId, Title = "In library", MediaKind = MediaKind.Manga, Status = UserAnimeStatus.Watching });
            setup.History.Add(new HistoryItem { AnimeId = AnimeId + 1, Timestamp = today });
            setup.Metadata.AddRange(
                new ShikiMetadata { Id = AnimeId, MediaKind = MediaKind.Manga, FetchedAt = today.AddDays(-31) },
                new ShikiMetadata { Id = AnimeId, MediaKind = MediaKind.Anime, FetchedAt = today.AddDays(-31) },
                new ShikiMetadata { Id = AnimeId + 1, MediaKind = MediaKind.Anime, FetchedAt = today.AddDays(-31) },
                new ShikiMetadata { Id = AnimeId + 2, FetchedAt = today.AddDays(-30) });
            setup.MalSearchCache.AddRange(
                new MalSearchCache { QueryNormalized = "old hit", AnimeId = AnimeId, CreatedAt = today.AddDays(-31) },
                new MalSearchCache { QueryNormalized = "kept hit", AnimeId = AnimeId, CreatedAt = today.AddDays(-30) },
                new MalSearchCache { QueryNormalized = "old miss", CreatedAt = today.AddDays(-8) },
                new MalSearchCache { QueryNormalized = "kept miss", CreatedAt = today.AddDays(-7) });
            setup.HttpResponseCache.AddRange(
                new HttpCacheEntry { UrlHash = "expired", CreatedAt = today.AddDays(-31) },
                new HttpCacheEntry { UrlHash = "kept", CreatedAt = today.AddDays(-30) });
            setup.ScrobbleProgress.AddRange(
                new ScrobbleProgressEntity { Key = "expired", UpdatedAt = today.AddDays(-31) },
                new ScrobbleProgressEntity { Key = "kept", UpdatedAt = today.AddDays(-30) });
            setup.PlaybackResume.AddRange(
                new PlaybackResumeEntryEntity { VideoUrl = "expired", UpdatedAt = today.AddDays(-91) },
                new PlaybackResumeEntryEntity { VideoUrl = "kept", UpdatedAt = today.AddDays(-90) });
            await setup.SaveChangesAsync(ct);
        }

        var maintenance = new DatabaseMaintenance(factory, new MaintenanceClock(MaintenanceNow));
        Assert.True(await maintenance.PerformAsync(ct));
        Assert.True(await maintenance.PerformAsync(ct));
        await using var verify = factory.CreateDbContext();
        var keys = await verify.Metadata.OrderBy(item => item.Id)
            .Select(item => new MediaKey(item.Id, item.MediaKind)).ToListAsync(ct);
        Assert.Equal([new(AnimeId, MediaKind.Manga), new(AnimeId + 1, MediaKind.Anime), new(AnimeId + 2, MediaKind.Anime)], keys);
        Assert.Equal(["kept hit", "kept miss"], await verify.MalSearchCache.OrderBy(item => item.QueryNormalized)
            .Select(item => item.QueryNormalized).ToListAsync(ct));
        Assert.Equal("kept", Assert.Single(await verify.HttpResponseCache.ToListAsync(ct)).UrlHash);
        Assert.Equal("kept", Assert.Single(await verify.ScrobbleProgress.ToListAsync(ct)).Key);
        Assert.Equal("kept", Assert.Single(await verify.PlaybackResume.ToListAsync(ct)).VideoUrl);
    }

    [Fact]
    public async Task SqliteMaintenance_WhenWriterOwnsWal_DefersAndRetriesWithoutLosingCommittedEdits()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        var factory = CreateFactory();
        await using (var setup = factory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync(ct);
            await setup.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
            setup.History.Add(new HistoryItem { AnimeId = AnimeId, Timestamp = MaintenanceNow.UtcDateTime.AddDays(-200) });
            await setup.SaveChangesAsync(ct);
        }

        var maintenance = new DatabaseMaintenance(factory, new MaintenanceClock(MaintenanceNow));
        await using (var writer = factory.CreateDbContext())
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(ct);
            writer.UserAnime.Add(new AnimeEntity { Id = AnimeId, Title = "Active edit", Status = UserAnimeStatus.Watching, Progress = 5 });
            writer.SyncTasks.Add(new SyncTaskEntity { AnimeId = AnimeId, Progress = 5 });
            await writer.SaveChangesAsync(ct);
            var cleanup = Task.Run(() => maintenance.PerformAsync(ct), ct);
            Assert.False(await cleanup.WaitAsync(TimeSpan.FromSeconds(5), ct));
            await transaction.CommitAsync(ct);
        }

        Assert.True(await maintenance.PerformAsync(ct));
        await using var verify = factory.CreateDbContext();
        Assert.Empty(await verify.History.ToListAsync(ct));
        Assert.Equal(5, Assert.Single(await verify.UserAnime.ToListAsync(ct)).Progress);
        Assert.Equal(5, Assert.Single(await verify.SyncTasks.ToListAsync(ct)).Progress);
    }

    [Fact]
    public async Task SqliteMaintenance_ShutdownInterruptsRunningCleanupAndRollsBack()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        var factory = CreateFactory();
        await using (var setup = factory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync(ct);
            setup.History.Add(new HistoryItem { Timestamp = MaintenanceNow.UtcDateTime.AddDays(-200) });
            setup.HttpResponseCache.Add(new HttpCacheEntry { UrlHash = "expired", CreatedAt = MaintenanceNow.UtcDateTime.AddDays(-90) });
            await setup.SaveChangesAsync(ct);
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER pause_cache_cleanup BEFORE DELETE ON http_response_cache
                BEGIN
                    SELECT wait_for_maintenance_cancel();
                    SELECT sum(n) FROM (
                        WITH RECURSIVE remaining(n) AS (
                            SELECT 1 UNION ALL SELECT n + 1 FROM remaining WHERE n < 10000000
                        ) SELECT n FROM remaining
                    );
                END;
                """, ct);
        }

        await using var originalContext = factory.CreateDbContext();
        await using var connection = new SqliteConnection(originalContext.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        using var resumeSql = new ManualResetEventSlim();
        var commandStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.CreateFunction("wait_for_maintenance_cancel", () =>
        {
            commandStarted.TrySetResult();
            resumeSql.Wait(ct);
            return 0;
        });
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var maintenance = new DatabaseMaintenance(new FileDbContextFactory(options), new MaintenanceClock(MaintenanceNow));
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var background = new BackgroundTaskSupervisor();
        using var service = new MaintenanceService([new MaintenanceProbe(async jobToken =>
        {
            using var registration = jobToken.Register(resumeSql.Set);
            try { await maintenance.PerformAsync(jobToken); }
            catch (OperationCanceledException) when (jobToken.IsCancellationRequested)
            {
                cancelled.TrySetResult();
                throw;
            }
        })], background);

        service.Start();
        await commandStarted.Task.WaitAsync(ct);
        Assert.False(await maintenance.PerformAsync(ct));
        await new BackgroundTasksShutdownHandler(background).FlushAsync().WaitAsync(ct);
        Assert.True(cancelled.Task.IsCompletedSuccessfully);
        await using var verify = factory.CreateDbContext();
        Assert.Single(await verify.History.ToListAsync(ct));
        Assert.Single(await verify.HttpResponseCache.ToListAsync(ct));

        await verify.Database.ExecuteSqlRawAsync("DROP TRIGGER pause_cache_cleanup", ct);
        Assert.True(await maintenance.PerformAsync(ct));
        Assert.Empty(await verify.History.ToListAsync(ct));
        Assert.Empty(await verify.HttpResponseCache.ToListAsync(ct));
    }

    [Fact]
    public async Task SqliteMaintenance_ShutdownCheckpointDefersForWalReaderAndDatabaseReopens()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        var factory = CreateFactory();
        var initializer = new DatabaseInitializer(factory);
        await initializer.InitializeAsync().WaitAsync(ct);
        await using (var reader = factory.CreateDbContext())
        {
            await reader.Database.OpenConnectionAsync(ct);
            var sqlite = Assert.IsType<SqliteConnection>(reader.Database.GetDbConnection());
            await using var readTransaction = sqlite.BeginTransaction(deferred: true);
            reader.Database.UseTransaction(readTransaction);
            Assert.Empty(await reader.UserAnime.ToListAsync(ct));

            await using (var writer = factory.CreateDbContext())
            {
                writer.UserAnime.Add(new AnimeEntity { Id = AnimeId, Title = "In WAL", Status = UserAnimeStatus.Watching, Progress = 8 });
                await writer.SaveChangesAsync(ct);
            }
            await new DatabaseShutdownHandler(initializer).FlushAsync().WaitAsync(TimeSpan.FromSeconds(5), ct);
        }

        await initializer.FlushAsync(ct);
        await using var reopened = factory.CreateDbContext();
        Assert.Equal(8, Assert.Single(await reopened.UserAnime.ToListAsync(ct)).Progress);
    }

    [Fact]
    public async Task SqliteMaintenance_VacuumUsesWeeklyCadenceAndKeepsLibraryData()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        var factory = CreateFactory();
        await using var setup = factory.CreateDbContext();
        await setup.Database.EnsureCreatedAsync(ct);
        setup.UserAnime.Add(new AnimeEntity { Id = AnimeId, Title = "Keep this", Status = UserAnimeStatus.Watching, Progress = 4 });
        await setup.SaveChangesAsync(ct);
        await setup.Database.ExecuteSqlRawAsync("""
            CREATE TABLE vacuum_test_pages (data BLOB);
            INSERT INTO vacuum_test_pages VALUES (zeroblob(262144));
            DROP TABLE vacuum_test_pages;
            """, ct);
        await setup.Database.OpenConnectionAsync(ct);
        var clock = new MaintenanceClock(MaintenanceNow);
        var maintenance = new DatabaseMaintenance(factory, clock);
        Assert.InRange(await ReadFreePagesAsync(), 1, 1000);
        Assert.True(await maintenance.PerformAsync(ct));
        Assert.True(await ReadFreePagesAsync() > 0);

        clock.Now = clock.Now.AddDays(8);
        Assert.True(await maintenance.PerformAsync(ct));
        Assert.Equal(0, await ReadFreePagesAsync());
        Assert.Equal(4, Assert.Single(await setup.UserAnime.AsNoTracking().ToListAsync(ct)).Progress);

        async Task<int> ReadFreePagesAsync()
        {
            await using var command = setup.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA freelist_count;";
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        }
    }

    private sealed class MaintenanceClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MaintenanceProbe(Func<CancellationToken, Task> execute) : IMaintenanceTask
    {
        public TimeSpan InitialDelay => TimeSpan.Zero;
        public TimeSpan Interval => TimeSpan.FromDays(1);
        public Task ExecuteAsync(CancellationToken ct) => execute(ct);
    }
}
