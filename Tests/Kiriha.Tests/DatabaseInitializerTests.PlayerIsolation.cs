using Kiriha.Core.Domain.Constants;
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
    public async Task PlayerMode_WithLockedLibrary_InitializesAndShutsDownWithoutMigrating()
    {
        var directory = Directory.CreateTempSubdirectory("Kiriha.PlayerIsolation.");
        var data = directory.CreateSubdirectory("data");
        var databasePath = Path.Combine(data.FullName, AppConstants.System.FileNames.Database);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString())
            .Options;
        var factory = new TestDbContextFactory(options);
        try
        {
            await using (var oldDatabase = factory.CreateDbContext())
                await oldDatabase.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await SeedLibraryAsync(factory);

            await using (File.Open(databasePath, FileMode.Open, FileAccess.Read, FileShare.None))
            using (var player = StartTestProcess(PlayerIsolationTestProcess.Command, directory.FullName,
                       "--player", Path.Combine(directory.FullName, "Frieren 01.mkv")))
            {
                try
                {
                    var output = player.StandardOutput.ReadToEndAsync();
                    var errors = player.StandardError.ReadToEndAsync();
                    await player.WaitForExitAsync().WaitAsync(MigrationTimeout);
                    Assert.True(player.ExitCode == 0, await errors);
                    Assert.Equal(PlayerIsolationTestProcess.Completed, (await output).Trim());
                }
                finally
                {
                    await StopMigrationProcessAsync(player);
                }
            }

            Assert.False(File.Exists(databasePath + ".migration-lock"));
            await using var unchanged = factory.CreateDbContext();
            Assert.Equal(PreviousMigration, (await unchanged.Database.GetAppliedMigrationsAsync()).Last());
            Assert.Equal(8, Assert.Single(await new UserAnimeRepository(factory).GetAllAsync()).Progress);
            Assert.Single(await new HistoryRepository(factory).GetAsync());
            Assert.Single(await new SyncTaskRepository(factory).GetPendingAsync());
        }
        finally
        {
            await DeleteMigrationDirectoryAsync(directory);
        }
    }
}
