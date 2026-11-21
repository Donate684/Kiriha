using System.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Data.Core;

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
    private DateTime _lastVacuum = DateTime.UtcNow;

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
        var orphanedMetadataCount = await context.Database.ExecuteSqlRawAsync("""
            DELETE FROM metadata
            WHERE fetched_at < date('now', '-30 days')
            AND NOT EXISTS (
                SELECT 1 FROM user_anime u
                WHERE u.id = metadata.id AND u.media_kind = metadata.media_kind
            )
            AND NOT EXISTS (
                SELECT 1 FROM history h
                WHERE h.anime_id = metadata.id AND h.media_kind = metadata.media_kind
            )
            """);
        if (orphanedMetadataCount > 0)
            Log.Information("DatabaseMaintenance: Removed {Count} orphaned metadata entries", orphanedMetadataCount);
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

        var scrobbleCleanupCount = await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM scrobble_progress WHERE updated_at < date('now', '-30 days')");
        if (scrobbleCleanupCount > 0)
            Log.Information("DatabaseMaintenance: Removed {Count} expired scrobble progress entries", scrobbleCleanupCount);

        var resumeCleanupCount = await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM playback_resume WHERE updated_at < date('now', '-90 days')");
        if (resumeCleanupCount > 0)
            Log.Information("DatabaseMaintenance: Removed {Count} expired playback resume entries", resumeCleanupCount);
    }

    private async Task OptimizeDatabaseAsync(AppDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync("ANALYZE;");

        if (await ShouldVacuumAsync(context))
        {
            Log.Information("DatabaseMaintenance: Running VACUUM (db is fragmented or weekly cadence reached)...");
            await context.Database.ExecuteSqlRawAsync("VACUUM;");
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA wal_autocheckpoint=1000;");
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
