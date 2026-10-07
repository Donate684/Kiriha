using System.Diagnostics;
using Kiriha.Core.Abstractions.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data.Core;

/// <summary>
/// Owns the schema lifecycle of the SQLite database. Strategy:
///   * Native SQLite PRAGMA user_version schema marking (v1.6 = 1600).
///   * Automatic legacy pre-1.6 schema detection and safe backup to *.pre16.bak.
///   * Unified EF Core baseline migration (v1.6) with zero runtime DDL workarounds.
///   * Single round-trip WAL and performance pragmas.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    public const int CurrentSchemaVersion = 1600;

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly TaskCompletionSource _initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _initStarted; // 0 = not started, 1 = started (Interlocked guard)

    public Task InitializationTask => _initTcs.Task;

    public DatabaseInitializer(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task InitializeAsync()
    {
        // Single-shot guard: every concurrent caller awaits the same TCS.
        if (Interlocked.CompareExchange(ref _initStarted, 1, 0) != 0)
        {
            await _initTcs.Task;
            return;
        }

        try
        {
            var total = Stopwatch.StartNew();

            // Detect and safely backup pre-v1.6 legacy database files
            await HandlePreV16DatabaseAsync();

            using var context = await _contextFactory.CreateDbContextAsync();

            var stage = Stopwatch.StartNew();
            await context.Database.MigrateAsync();
            Log.Information("StartupTiming: database migrations elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            // WAL + sane defaults + user_version in a single batch (one round-trip on cold start).
            stage.Restart();
            await context.Database.ExecuteSqlRawAsync(
                $"PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA wal_autocheckpoint=1000; PRAGMA user_version = {CurrentSchemaVersion};");
            Log.Information("StartupTiming: database pragmas elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            await LegacyUserStateMigration.MigrateAsync(context);

            Log.Information("Database initialized elapsedMs={ElapsedMs}", total.ElapsedMilliseconds);

            _initTcs.TrySetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize EF Core database");
            _initTcs.TrySetException(ex);
        }
    }

    private async Task HandlePreV16DatabaseAsync()
    {
        string? dbPath = null;
        try
        {
            using var probeContext = await _contextFactory.CreateDbContextAsync();
            var connection = probeContext.Database.GetDbConnection();
            dbPath = connection.DataSource;

            if (string.IsNullOrWhiteSpace(dbPath) ||
                dbPath.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
                dbPath.Contains("mode=memory", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(dbPath))
            {
                // New database or in-memory DB; no pre-existing legacy file on disk
                return;
            }

            // Database file exists. Check its user_version.
            int userVersion = 0;
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA user_version;";
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    userVersion = Convert.ToInt32(result);
                }
            }

            await connection.CloseAsync();
            SqliteConnection.ClearAllPools();

            if (userVersion < CurrentSchemaVersion)
            {
                Log.Warning("Detected legacy pre-1.6 database schema (version {Version}). Forcing recreate with backup.", userVersion);

                string bakPath = dbPath + ".pre16.bak";
                File.Move(dbPath, bakPath, overwrite: true);

                var walPath = dbPath + "-wal";
                if (File.Exists(walPath)) try { File.Delete(walPath); } catch { }

                var shmPath = dbPath + "-shm";
                if (File.Exists(shmPath)) try { File.Delete(shmPath); } catch { }

                Log.Information("Legacy database safely backed up to {BakPath} and ready for pristine v1.6 recreation", bakPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to probe or backup database at {Path}; proceeding with standard initialization", dbPath);
        }
    }

    /// <summary>
    /// Forces all pending WAL frames into the main database file. Call before
    /// app exit and on system session-end to guarantee durability against a
    /// forced <c>TerminateProcess</c>.
    /// </summary>
    public async Task FlushAsync()
    {
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);");
            Log.Information("DatabaseInitializer: WAL checkpoint(TRUNCATE) completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DatabaseInitializer: FlushAsync failed");
        }
    }
}
