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

            using var context = await _contextFactory.CreateDbContextAsync();

            var stage = Stopwatch.StartNew();
            await context.Database.MigrateAsync();
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
