using System.Data.Common;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using IMigrator = Microsoft.EntityFrameworkCore.Migrations.IMigrator;

namespace Kiriha.Tests;

public sealed partial class DatabaseInitializerTests
{
    private const string PreviousMigration = "20261116105000_CoreSchemaCleanup";
    private const string StorageMigration = "20261116120101_Wave3StorageUnification";
    private static readonly TimeSpan MigrationTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task InitializeAsync_CreatesMigratedDatabaseAndIsIdempotent()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", Guid.NewGuid() + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var factory = new TestDbContextFactory(options);
        var initializer = new DatabaseInitializer(factory);

        try
        {
            await initializer.InitializeAsync();
            await initializer.InitializeAsync();

            await using var context = new AppDbContext(options);

            Assert.True(await TableExistsAsync(context, "user_anime"));
            Assert.True(await TableExistsAsync(context, "history"));
            Assert.True(await TableExistsAsync(context, "sync_tasks"));
            Assert.True(await TableExistsAsync(context, "hidden_seasonal_anime"));
            Assert.True(await TableExistsAsync(context, "hidden_torrent_anime"));
            Assert.True(await TableExistsAsync(context, "torrent_title_filters"));
            Assert.True(await TableExistsAsync(context, "anime_country_origin"));
            Assert.True(await TableExistsAsync(context, "title_mappings"));
            Assert.True(await TableExistsAsync(context, "scrobble_progress"));
            Assert.True(await TableExistsAsync(context, "playback_resume"));
            Assert.True(await TableExistsAsync(context, "anilist_media_map"));
            Assert.True(await TableExistsAsync(context, "__EFMigrationsHistory"));

            // Verify history table has tracker_status_json and poster_url columns
            Assert.True(await ColumnExistsAsync(context, "history", "tracker_status_json"));
            Assert.True(await ColumnExistsAsync(context, "history", "poster_url"));
            Assert.True(await ColumnExistsAsync(context, "metadata", "media_kind"));

            // Verify PRAGMA user_version is stamped with current schema version marker
            Assert.Equal(DatabaseInitializer.CurrentSchemaVersion, await GetUserVersionAsync(context));
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
            try { File.Delete(dbPath + "-wal"); } catch { }
            try { File.Delete(dbPath + "-shm"); } catch { }
            File.Delete(dbPath + ".migration-lock");
        }
    }

    [Fact]
    public async Task FlushAsync_CheckpointsWalWithoutThrowing()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", Guid.NewGuid() + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var initializer = new DatabaseInitializer(new TestDbContextFactory(options));

        try
        {
            await initializer.InitializeAsync();
            await initializer.FlushAsync();

            Assert.True(File.Exists(dbPath));
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
            try { File.Delete(dbPath + "-wal"); } catch { }
            try { File.Delete(dbPath + "-shm"); } catch { }
            File.Delete(dbPath + ".migration-lock");
        }
    }

    [Fact]
    public async Task InitializeAsync_AfterInterruptedMigration_RestartsOnSameFileAndRestoresQueue()
    {
        var directory = Directory.CreateTempSubdirectory("Kiriha.MigrationRestart.");
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory.FullName, "library.db"),
            Pooling = false
        }.ToString();
        var originalFactory = CreateFactory();
        var interruption = new InterruptStorageMigration();
        var interrupted = new DatabaseInitializer(CreateFactory(interruption));

        try
        {
            await using (var previous = originalFactory.CreateDbContext())
            {
                await previous.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            }
            await SeedLibraryAsync(originalFactory);

            var firstFailure = await Assert.ThrowsAsync<SqliteException>(
                () => interrupted.InitializeAsync().WaitAsync(MigrationTimeout));
            var readinessFailure = await Assert.ThrowsAsync<SqliteException>(
                () => interrupted.InitializationTask.WaitAsync(MigrationTimeout));
            var subsequentFailure = await Assert.ThrowsAsync<SqliteException>(
                () => interrupted.InitializeAsync().WaitAsync(MigrationTimeout));
            Assert.Same(interruption.Error, readinessFailure);
            Assert.Same(readinessFailure, subsequentFailure);
            Assert.Same(readinessFailure, firstFailure);

            await using (var rolledBack = originalFactory.CreateDbContext())
            {
                Assert.Equal(PreviousMigration, (await rolledBack.Database.GetAppliedMigrationsAsync()).Last());
                Assert.False(await ColumnExistsAsync(rolledBack, "metadata", "media_kind"));
                Assert.False(await TableExistsAsync(rolledBack, "anilist_media_map"));
            }

            var restartedFactory = CreateFactory();
            var restarted = new DatabaseInitializer(restartedFactory);
            await restarted.InitializeAsync().WaitAsync(MigrationTimeout);
            await restarted.InitializationTask.WaitAsync(MigrationTimeout);
            await using (var migrated = restartedFactory.CreateDbContext())
            {
                Assert.Equal(StorageMigration, (await migrated.Database.GetAppliedMigrationsAsync()).Last());
                Assert.Empty(await migrated.Database.GetPendingMigrationsAsync());
                Assert.Equal(DatabaseInitializer.CurrentSchemaVersion, await GetUserVersionAsync(migrated));
            }

            var allAnime = await new UserAnimeRepository(restartedFactory).GetAllAsync();
            var anime = Assert.Single(allAnime);
            Assert.Equal(8, anime.Progress);
            Assert.Equal(UserAnimeStatus.Watching, anime.Status);
            var queue = new SyncTaskRepository(restartedFactory);
            var task = Assert.Single(await queue.GetPendingAsync());
            Assert.Equal(anime.Id, task.AnimeId);
            Assert.Equal(8, task.Progress);
            Assert.Equal(1, task.RetryCount);
            var history = new HistoryService(new HistoryRepository(restartedFactory));
            var savedEntry = Assert.Single(await history.GetHistoryAsync());
            Assert.Equal(anime.Id, savedEntry.AnimeId);
            Assert.Equal(8, savedEntry.Episode);
            Assert.Equal(TrackerSyncState.Success, savedEntry.TrackerStatuses["Shikimori"].State);
            Assert.Equal(TrackerSyncState.Retrying, savedEntry.TrackerStatuses["MyAnimeList"].State);

            await ReplayPendingTaskAsync(restarted, queue, history, anime.Id);
            var entryAfterSync = Assert.Single(await history.GetHistoryAsync());
            Assert.Equal(savedEntry.Id, entryAfterSync.Id);
            Assert.Equal(TrackerSyncState.Success, entryAfterSync.TrackerStatuses["MyAnimeList"].State);
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        TestDbContextFactory CreateFactory(params IInterceptor[] interceptors) =>
            new(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(interceptors)
                .Options);
    }

    private static async Task SeedLibraryAsync(TestDbContextFactory factory)
    {
        const int animeId = 777;
        const string title = "Hagane no Renkinjutsushi";
        await new UserAnimeRepository(factory).UpsertAsync(new AnimeEntity
        {
            Id = animeId, Title = title, Progress = 8, Status = UserAnimeStatus.Watching
        });
        var entry = new HistoryItem
        {
            AnimeId = animeId, AnimeTitle = title, Episode = 8,
            ActionType = (int)HistoryActionType.Scrobbled, Timestamp = DateTime.UtcNow
        };
        entry.SetTrackerStatus("Shikimori", TrackerSyncState.Success);
        entry.SetTrackerStatus("MyAnimeList", TrackerSyncState.Retrying);
        await new HistoryRepository(factory).AddAsync(entry);
        await new SyncTaskRepository(factory).AddAsync(new SyncTaskEntity
        {
            AnimeId = animeId, Type = "UpdateProgress", Progress = 8,
            Status = StatusMapper.ToDbString(UserAnimeStatus.Watching),
            RetryCount = 1, SuccessfulTrackersJson = """["Shikimori"]"""
        });
    }

    private static async Task ReplayPendingTaskAsync(
        DatabaseInitializer initializer, SyncTaskRepository queue, HistoryService history, int animeId)
    {
        var shiki = new Mock<ITrackerService>();
        shiki.SetupGet(t => t.Name).Returns("Shikimori");
        shiki.SetupGet(t => t.IsEnabled).Returns(true);
        var mal = new Mock<ITrackerService>();
        mal.SetupGet(t => t.Name).Returns("MyAnimeList");
        mal.SetupGet(t => t.IsEnabled).Returns(true);
        mal.Setup(t => t.UpdateProgressAsync(
                animeId, 8, UserAnimeStatus.Watching, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SyncOutcome.Success);

        using var background = new BackgroundTaskSupervisor();
        var sync = new SyncManager([shiki.Object, mal.Object], queue, initializer, history, background);
        await sync.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(MigrationTimeout);
        try
        {
            while ((await queue.GetPendingAsync(timeout.Token)).Count > 0)
                await Task.Delay(20, timeout.Token);
        }
        finally
        {
            await sync.StopAsync(CancellationToken.None);
            await history.FlushAsync(MigrationTimeout);
        }
        shiki.Verify(t => t.UpdateProgressAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<UserAnimeStatus?>(), It.IsAny<int?>(),
            It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        mal.Verify(t => t.UpdateProgressAsync(
            animeId, 8, UserAnimeStatus.Watching, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class InterruptStorageMigration : DbCommandInterceptor
    {
        public SqliteException Error { get; } = new("Simulated failure writing the migrated schema", 10);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            // The storage migration rebuilds metadata and creates the AniList map before this statement.
            if (command.CommandText.Contains("CREATE TABLE \"playback_resume\"", StringComparison.Ordinal))
                throw Error;

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private static async Task<bool> TableExistsAsync(AppDbContext context, string tableName)
    {
        var conn = context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name = $name;";
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        cmd.Parameters.Add(parameter);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    private static async Task<bool> ColumnExistsAsync(AppDbContext context, string tableName, string columnName)
    {
        var conn = context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static async Task<int> GetUserVersionAsync(AppDbContext context)
    {
        var conn = context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AppDbContext> options)
        {
            _options = options;
        }

        public AppDbContext CreateDbContext() => new(_options);
    }
}
