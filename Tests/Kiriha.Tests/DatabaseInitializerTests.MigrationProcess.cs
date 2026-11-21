using System.Diagnostics;
using Kiriha.Data.Core;
using Kiriha.Data.Repository;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using IMigrator = Microsoft.EntityFrameworkCore.Migrations.IMigrator;

namespace Kiriha.Tests;

public sealed partial class DatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_AfterMigrationProcessIsKilled_RecoversSameDatabase()
    {
        var directory = Directory.CreateTempSubdirectory("Kiriha.MigrationProcess.");
        var databasePath = Path.Combine(directory.FullName, "library.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString())
            .Options;
        var factory = new TestDbContextFactory(options);

        try
        {
            await using (var previous = factory.CreateDbContext())
                await previous.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await SeedLibraryAsync(factory);

            using (var writer = StartMigrationProcess(databasePath, "pause"))
            {
                try
                {
                    using var timeout = new CancellationTokenSource(MigrationTimeout);
                    var pauseMessage = await writer.StandardOutput.ReadLineAsync(timeout.Token);
                    Assert.Equal(MigrationTestProcess.Paused, pauseMessage);

                    using var contender = StartMigrationProcess(databasePath, "resume");
                    try
                    {
                        await contender.WaitForExitAsync().WaitAsync(MigrationTimeout);
                        Assert.NotEqual(0, contender.ExitCode);
                        Assert.Contains(".migration-lock", await contender.StandardError.ReadToEndAsync(), StringComparison.Ordinal);
                        Assert.False(writer.HasExited);
                    }
                    finally
                    {
                        await StopMigrationProcessAsync(contender);
                    }
                }
                finally
                {
                    await StopMigrationProcessAsync(writer);
                }
            }

            // Let the restarted initializer be the first SQLite client to open the file after the crash.
            using (var restarted = StartMigrationProcess(databasePath, "resume"))
            {
                try
                {
                    await restarted.WaitForExitAsync().WaitAsync(MigrationTimeout);
                    Assert.True(restarted.ExitCode == 0, await restarted.StandardError.ReadToEndAsync());
                    Assert.Equal(MigrationTestProcess.ReplayVerified, (await restarted.StandardOutput.ReadToEndAsync()).Trim());
                }
                finally
                {
                    await StopMigrationProcessAsync(restarted);
                }
            }

            await using (var migrated = factory.CreateDbContext())
            {
                Assert.Empty(await migrated.Database.GetPendingMigrationsAsync());
                Assert.Equal(DatabaseInitializer.CurrentSchemaVersion, await GetUserVersionAsync(migrated));
                Assert.Equal(0, await CountMigrationLocksAsync(migrated));
            }
            var anime = Assert.Single(await new UserAnimeRepository(factory).GetAllAsync());
            Assert.Equal(8, anime.Progress);
            var entry = Assert.Single(await new HistoryRepository(factory).GetAsync());
            Assert.Equal(anime.Id, entry.AnimeId);
            Assert.Equal(8, entry.Episode);
            var task = Assert.Single(await new SyncTaskRepository(factory).GetPendingAsync());
            Assert.Equal(anime.Id, task.AnimeId);
            Assert.Equal(8, task.Progress);
            Assert.Equal("""["Shikimori"]""", task.SuccessfulTrackersJson);
        }
        finally
        {
            await DeleteMigrationDirectoryAsync(directory);
        }
    }

    [Fact]
    public async Task InitializeAsync_WithUnownedEfLock_ReportsFailureWithoutDeletingLock()
    {
        var directory = Directory.CreateTempSubdirectory("Kiriha.ForeignMigrationLock.");
        var databasePath = Path.Combine(directory.FullName, "library.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString())
            .Options;
        var factory = new TestDbContextFactory(options);

        try
        {
            // A non-Kiriha EF client owns the lock row. No matching token exists in our file.
            await using (var previous = factory.CreateDbContext())
            {
                await previous.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                await previous.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "__EFMigrationsLock"("Id", "Timestamp") VALUES (1, 'external-owner');
                    """);
            }
            await File.WriteAllBytesAsync(databasePath + ".migration-lock", Guid.NewGuid().ToByteArray());

            var initializer = new DatabaseInitializer(factory);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => initializer.InitializeAsync().WaitAsync(MigrationTimeout));
            Assert.Contains("owner Kiriha cannot verify", error.Message, StringComparison.Ordinal);

            await using var untouched = factory.CreateDbContext();
            Assert.Equal(PreviousMigration, (await untouched.Database.GetAppliedMigrationsAsync()).Last());
            Assert.Equal(1, await CountMigrationLocksAsync(untouched));
        }
        finally
        {
            await DeleteMigrationDirectoryAsync(directory);
        }
    }

    private static Task<int> CountMigrationLocksAsync(AppDbContext context) =>
        context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM __EFMigrationsLock").SingleAsync();

    private static Process StartMigrationProcess(string databasePath, string mode) =>
        StartTestProcess(MigrationTestProcess.Command, databasePath, mode);

    private static Process StartTestProcess(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(MigrationTestProcess).Assembly.Location);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the test process.");
    }

    private static async Task StopMigrationProcessAsync(Process process)
    {
        if (!process.HasExited)
            process.Kill();
        await process.WaitForExitAsync().WaitAsync(MigrationTimeout);
    }

    private static async Task DeleteMigrationDirectoryAsync(DirectoryInfo directory)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                directory.Delete(recursive: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                // Windows can release SQLite file handles just after the killed process signals exit.
                await Task.Delay(50);
            }
        }
    }
}
