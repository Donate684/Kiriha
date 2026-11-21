using System.Diagnostics;
using Kiriha.Core.Abstractions.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Data.Core;

/// <summary>
/// Owns the schema lifecycle of the SQLite database (minimum supported baseline: v1.8+).
/// Applies EF Core migrations and configures WAL + performance pragmas in a single round-trip.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    public const int CurrentSchemaVersion = 1810;

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly TaskCompletionSource _initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _initStarted; // 0 = not started, 1 = started (Interlocked guard)

    public Task InitializationTask => _initTcs.Task;

    public DatabaseInitializer(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

#pragma warning disable S2077 // ExecuteSqlRawAsync executes PRAGMA statements with internal constant integer schema version
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

            await using var context = await _contextFactory.CreateDbContextAsync();
            await context.Database.OpenConnectionAsync();
            var connection = (SqliteConnection)context.Database.GetDbConnection();
            await using var migrationLock = SqliteMigrationLock.Open(connection);
            if (migrationLock != null)
                await migrationLock.RecoverAsync(connection);
            context.ActiveMigrationLock = migrationLock;

            var stage = Stopwatch.StartNew();
            await context.Database.MigrateAsync();
            migrationLock?.ClearOwner();
            Log.Information("StartupTiming: database migrations elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            // WAL + sane defaults + user_version in a single batch (one round-trip on cold start).
            stage.Restart();
            await context.Database.ExecuteSqlRawAsync(
                $"PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA wal_autocheckpoint=1000; PRAGMA user_version = {CurrentSchemaVersion};");
            Log.Information("StartupTiming: database pragmas elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            Log.Information("Database initialized elapsedMs={ElapsedMs}", total.ElapsedMilliseconds);

            _initTcs.TrySetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize EF Core database");
            _initTcs.TrySetException(ex);
        }

        // The caller that starts initialization must observe the same outcome as all waiters.
        await InitializationTask;
    }
#pragma warning restore S2077

    /// <summary>
    /// Attempts to checkpoint and truncate the WAL before exit. When another
    /// connection is still active, committed frames remain in the WAL for recovery.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            await using var connection = await SqliteMaintenanceConnection.OpenAsync(context, ct);
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await using var result = await command.ExecuteReaderAsync(ct);
            if (await result.ReadAsync(ct) && result.GetInt32(0) == 0)
                Log.Information("DatabaseInitializer: WAL checkpoint completed");
            else
                Log.Information("DatabaseInitializer: WAL checkpoint deferred because SQLite is busy");
        }
        catch (SqliteException ex) when (!ct.IsCancellationRequested &&
            ex.SqliteErrorCode is SqliteMaintenanceConnection.Busy or SqliteMaintenanceConnection.Locked)
        {
            Log.Information(ex, "DatabaseInitializer: WAL checkpoint deferred because SQLite is locked");
        }
        catch (SqliteException ex) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("SQLite checkpoint was cancelled.", ex, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "DatabaseInitializer: FlushAsync failed");
        }
    }
}
