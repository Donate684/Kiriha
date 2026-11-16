using Kiriha.Data.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Tests;

public sealed class DatabaseInitializerTests
{
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
