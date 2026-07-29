using Kiriha.Services.Data.Core;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data.Core;

/// <summary>
/// Periodic database hygiene: orphan/expired-row pruning, ANALYZE, conditional
/// VACUUM. Split out of <see cref="DatabaseService"/> so the CRUD path doesn't
/// carry maintenance state (the <c>_lastVacuum</c> field) and so the cadence
/// is owned by a single class instead of scattered across the file.
///
/// Lifecycle: invoked from <see cref="MaintenanceService"/> on its own daily
/// cadence — never block the UI thread on this. VACUUM is gated on
/// freelist size or a 7-day clock to avoid rewriting the whole DB on every run.
/// </summary>
public sealed class DatabaseMaintenance
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private DateTime _lastVacuum = DateTime.MinValue;

    public DatabaseMaintenance(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task PerformAsync()
    {
        Log.Information("DatabaseMaintenance: Starting database maintenance and cleanup...");
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            await CleanOrphanedMetadataAsync(context);
            await CleanOrphanedEpisodeReleasesAsync(context);
            await CleanFailedSyncTasksAsync(context);
            await CleanOldHistoryAsync(context);
            await CleanCacheTablesAsync(context);
            await OptimizeDatabaseAsync(context);

            Log.Information("DatabaseMaintenance: Database maintenance completed successfully.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DatabaseMaintenance: Error during database maintenance");
        }
    }

    private async Task CleanOrphanedMetadataAsync(AppDbContext context)
    {
        var orphanedMetadataCount = await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM metadata WHERE id NOT IN (SELECT id FROM user_anime)");
        if (orphanedMetadataCount > 0)
            Log.Information("DatabaseMaintenance: Removed {Count} orphaned metadata entries", orphanedMetadataCount);
    }

    private async Task CleanOrphanedEpisodeReleasesAsync(AppDbContext context)
    {
        var oldEpisodesCount = await context.Database.ExecuteSqlRawAsync(@"
            DELETE FROM episode_releases 
            WHERE mal_id NOT IN (SELECT id FROM user_anime)
            OR mal_id IN (SELECT id FROM user_anime WHERE status_detailed = 'finished_airing' AND airing_date < date('now', '-30 days'))");
        if (oldEpisodesCount > 0)
            Log.Information("DatabaseMaintenance: Removed {Count} outdated or orphaned episode release entries", oldEpisodesCount);
    }

    private async Task CleanFailedSyncTasksAsync(AppDbContext context)
    {
        var stuckTasks = await context.SyncTasks
            .Where(t => t.RetryCount >= 5)
            .ToListAsync();
        if (stuckTasks.Count > 0)
        {
            foreach (var t in stuckTasks)
            {
                context.History.Add(new HistoryItem
                {
                    AnimeId = t.AnimeId,
                    AnimeTitle = $"ID {t.AnimeId}",
                    RussianTitle = null,
                    Episode = t.Progress ?? 0,
                    Timestamp = DateTime.UtcNow,
                    ActionType = 3,
                    Detail = "SyncFailed (max retries exceeded)"
                });
            }
            context.SyncTasks.RemoveRange(stuckTasks);
            await context.SaveChangesAsync();
            Log.Information("DatabaseMaintenance: Removed {Count} permanently failed sync tasks", stuckTasks.Count);
        }
    }

    private async Task CleanOldHistoryAsync(AppDbContext context)
    {
        var historyCleanupCount = await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM history WHERE timestamp < date('now', '-180 days')");
        if (historyCleanupCount > 0)
            Log.Information("DatabaseMaintenance: Removed {Count} old history entries", historyCleanupCount);
    }

    private async Task CleanCacheTablesAsync(AppDbContext context)
    {
        var fileCacheCount = await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM file_recognition_cache WHERE last_used != '' AND last_used < date('now', '-90 days')");
        if (fileCacheCount > 0)
            Log.Information("DatabaseMaintenance: Removed {Count} stale file recognition cache entries", fileCacheCount);

        var malCacheCount = await context.Database.ExecuteSqlRawAsync(@"
            DELETE FROM mal_search_cache
            WHERE (anime_id <> 0 AND created_at < date('now', '-30 days'))
               OR (anime_id =  0 AND created_at < date('now', '-7 days'))");
        if (malCacheCount > 0)
            Log.Information("DatabaseMaintenance: Removed {Count} expired MAL search cache entries", malCacheCount);

        var httpCacheCount = await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM http_response_cache WHERE created_at < date('now', '-30 days')");
        if (httpCacheCount > 0)
            Log.Information("DatabaseMaintenance: Removed {Count} expired HTTP cache entries", httpCacheCount);
    }

    private async Task OptimizeDatabaseAsync(AppDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync("ANALYZE;");

        if (await ShouldVacuumAsync(context))
        {
            Log.Information("DatabaseMaintenance: Running VACUUM (db is fragmented or weekly cadence reached)...");
            await context.Database.ExecuteSqlRawAsync("VACUUM;");
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;");
            await context.Database.ExecuteSqlRawAsync("PRAGMA wal_autocheckpoint=200;");
            _lastVacuum = DateTime.UtcNow;
        }
    }

    private async Task<bool> ShouldVacuumAsync(AppDbContext context)
    {
        if (DateTime.UtcNow - _lastVacuum >= TimeSpan.FromDays(7))
            return true;

        try
        {
            var conn = context.Database.GetDbConnection();
            bool wasClosed = conn.State == ConnectionState.Closed;
            if (wasClosed) await conn.OpenAsync();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA freelist_count;";
                var freelist = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
                return freelist > 1000;
            }
            finally
            {
                if (wasClosed) await conn.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "DatabaseMaintenance: freelist probe failed, skipping VACUUM this cycle");
            return false;
        }
    }
}
