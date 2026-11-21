using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Data.Core;

/// <summary>Prunes expired database data and maintains SQLite without delaying foreground writers.</summary>
public sealed class DatabaseMaintenance
{
    private static readonly TimeSpan VacuumInterval = TimeSpan.FromDays(7);
    private static readonly TimeSpan OptimizationBudget = TimeSpan.FromSeconds(2);
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly TimeProvider _clock;
    private DateTimeOffset _lastVacuum;
    private int _running;

    public DatabaseMaintenance(IDbContextFactory<AppDbContext> contextFactory, TimeProvider? clock = null)
    {
        _contextFactory = contextFactory;
        _clock = clock ?? TimeProvider.System;
        _lastVacuum = _clock.GetUtcNow();
    }

    public async Task<bool> PerformAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            await using var connection = await SqliteMaintenanceConnection.OpenAsync(context, ct);
            await PruneAsync(context, connection, _clock.GetUtcNow().UtcDateTime, ct);
            await OptimizeAsync(context, connection, ct);
            return true;
        }
        catch (SqliteException ex) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("SQLite maintenance was cancelled.", ex, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteMaintenanceConnection.Busy or SqliteMaintenanceConnection.Locked)
        {
            Log.Information(ex, "DatabaseMaintenance: SQLite is busy. Cleanup will retry at the next scheduled run.");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DatabaseMaintenance: Cleanup failed");
            return false;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private static async Task PruneAsync(
        AppDbContext context, SqliteMaintenanceConnection connection, DateTime now, CancellationToken ct)
    {
        var today = now.Date;
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        int historyRemoved;
        int metadataRemoved;
        int cacheRemoved;
        using (connection.InterruptWhenCancelled(ct))
        {
            historyRemoved = await HistoryCleanup.PruneAsync(context, today.AddDays(-180), ct);
            metadataRemoved = await context.Database.ExecuteSqlAsync($"""
                DELETE FROM metadata
                WHERE fetched_at < {today.AddDays(-30)}
                AND NOT EXISTS (
                    SELECT 1 FROM user_anime u
                    WHERE u.id = metadata.id AND u.media_kind = metadata.media_kind
                )
                AND NOT EXISTS (
                    SELECT 1 FROM history h
                    WHERE h.anime_id = metadata.id AND h.media_kind = metadata.media_kind
                )
                """, ct);
            cacheRemoved = await PruneCachesAsync(context, now, ct);
        }

        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        Log.Information("DatabaseMaintenance: Removed {History} history, {Metadata} metadata and {Cache} cache entries",
            historyRemoved, metadataRemoved, cacheRemoved);
    }

    private static async Task<int> PruneCachesAsync(AppDbContext context, DateTime now, CancellationToken ct)
    {
        var today = now.Date;
        var monthCutoff = today.AddDays(-30);
        int removed = await context.Database.ExecuteSqlAsync($"""
            DELETE FROM mal_search_cache
            WHERE (anime_id <> 0 AND created_at < {monthCutoff})
               OR (anime_id = 0 AND created_at < {now - Repository.MalSearchCacheRepository.NegativeTtl})
            """, ct);
        removed += await context.Database.ExecuteSqlAsync(
            $"DELETE FROM http_response_cache WHERE created_at < {now - Repository.HttpCacheRepository.Retention}", ct);
        removed += await context.Database.ExecuteSqlAsync(
            $"DELETE FROM scrobble_progress WHERE updated_at < {monthCutoff}", ct);
        removed += await context.Database.ExecuteSqlAsync(
            $"DELETE FROM playback_resume WHERE updated_at < {today.AddDays(-90)}", ct);
        return removed;
    }

    private async Task OptimizeAsync(AppDbContext context, SqliteMaintenanceConnection connection, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(OptimizationBudget);
        var optimizationCt = budget.Token;
        try
        {
            using var progress = connection.InterruptWhenCancelled(optimizationCt);
            await context.Database.ExecuteSqlRawAsync("PRAGMA optimize;", optimizationCt);
            if (await ShouldVacuumAsync(context, optimizationCt))
            {
                await context.Database.ExecuteSqlRawAsync("VACUUM;", optimizationCt);
                _lastVacuum = _clock.GetUtcNow();
            }
        }
        catch (SqliteException ex) when (!ct.IsCancellationRequested &&
            (ex.SqliteErrorCode is SqliteMaintenanceConnection.Busy or SqliteMaintenanceConnection.Locked ||
                budget.IsCancellationRequested && ex.SqliteErrorCode == SqliteMaintenanceConnection.Interrupted))
        {
            Log.Debug(ex, "DatabaseMaintenance: Optimization deferred");
        }
        catch (OperationCanceledException ex) when (budget.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            Log.Debug(ex, "DatabaseMaintenance: Optimization budget reached");
        }
    }

    private async Task<bool> ShouldVacuumAsync(AppDbContext context, CancellationToken ct)
    {
        if (_clock.GetUtcNow() - _lastVacuum >= VacuumInterval)
            return true;

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA freelist_count;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct) ?? 0) > 1000;
    }
}
