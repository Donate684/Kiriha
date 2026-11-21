using Kiriha.Core.Abstractions.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Data.Core;

public enum CacheCleanupTarget
{
    History,
    ImageFiles,
    ApiCache,
    RecognitionCache,
    SeasonalCache
}

public sealed record CacheCleanupStats(CacheCleanupTarget Target, int ItemCount, long SizeBytes);

public sealed class CacheCleanupService
{
    private static readonly TimeSpan HistoryFlushTimeout = TimeSpan.FromSeconds(5);
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IHistoryService _historyService;
    private readonly string? _imageCachePath;
    private readonly string? _seasonalCachePath;

    public CacheCleanupService(
        IDbContextFactory<AppDbContext> contextFactory,
        IHistoryService historyService,
        string? imageCachePath = null,
        string? seasonalCachePath = null)
    {
        _contextFactory = contextFactory;
        _historyService = historyService;
        _imageCachePath = imageCachePath;
        _seasonalCachePath = seasonalCachePath;
    }

    public Task<IReadOnlyList<CacheCleanupStats>> GetStatsAsync(CancellationToken ct = default) =>
        Task.Run(() => ReadStatsAsync(ct), ct);

    private async Task<IReadOnlyList<CacheCleanupStats>> ReadStatsAsync(CancellationToken ct)
    {
        var stats = new List<CacheCleanupStats>();

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await using var connection = await SqliteMaintenanceConnection.OpenAsync(context, ct);

        stats.Add(new CacheCleanupStats(
            CacheCleanupTarget.History,
            await context.History.CountAsync(ct),
            0));

        stats.Add(GetDirectoryStats(CacheCleanupTarget.ImageFiles, _imageCachePath));

        var apiCount =
            await context.Metadata.CountAsync(ct) +
            await context.HttpResponseCache.CountAsync(ct) +
            await context.AnimeRelations.CountAsync(ct) +
            await context.AnimeCountryOrigin.CountAsync(ct) +
            await context.AniListMediaMap.CountAsync(ct);
        var httpBytes = await context.HttpResponseCache
            .AsNoTracking()
            .SumAsync(x => (long)x.Body.Length, ct);
        stats.Add(new CacheCleanupStats(
            CacheCleanupTarget.ApiCache,
            apiCount,
            httpBytes));

        stats.Add(new CacheCleanupStats(
            CacheCleanupTarget.RecognitionCache,
            await context.MalSearchCache.CountAsync(ct),
            0));

        stats.Add(GetDirectoryStats(CacheCleanupTarget.SeasonalCache, _seasonalCachePath));

        return stats;
    }

    public async Task ClearAsync(IEnumerable<CacheCleanupTarget> targets, CancellationToken ct = default)
    {
        var requested = targets.Distinct().ToHashSet();
        if (requested.Count == 0) return;

        if (requested.Contains(CacheCleanupTarget.History))
            await _historyService.FlushAsync(HistoryFlushTimeout).WaitAsync(ct);

        try
        {
            await Task.Run(() => ClearStorageAsync(requested, ct), ct);
        }
        catch (SqliteException ex) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("Cache cleanup was cancelled.", ex, ct);
        }
    }

    private async Task ClearStorageAsync(HashSet<CacheCleanupTarget> requested, CancellationToken ct)
    {
        if (requested.Overlaps([CacheCleanupTarget.History, CacheCleanupTarget.ApiCache, CacheCleanupTarget.RecognitionCache]))
            await ClearDatabaseAsync(requested, ct);

        if (requested.Contains(CacheCleanupTarget.ImageFiles))
            DeleteDirectoryContents(_imageCachePath, ct);

        if (requested.Contains(CacheCleanupTarget.SeasonalCache))
            DeleteDirectoryContents(_seasonalCachePath, ct);
    }

    private async Task ClearDatabaseAsync(HashSet<CacheCleanupTarget> requested, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await using var connection = await SqliteMaintenanceConnection.OpenAsync(context, ct);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        using (connection.InterruptWhenCancelled(ct))
        {
            if (requested.Contains(CacheCleanupTarget.History))
                await HistoryCleanup.PruneAsync(context, cutoff: null, ct);

            if (requested.Contains(CacheCleanupTarget.ApiCache))
                await context.Database.ExecuteSqlRawAsync("""
                    DELETE FROM metadata;
                    DELETE FROM http_response_cache;
                    DELETE FROM anime_relations;
                    DELETE FROM anime_relation_meta;
                    DELETE FROM anime_country_origin;
                    DELETE FROM anilist_media_map;
                    """, ct);

            if (requested.Contains(CacheCleanupTarget.RecognitionCache))
                await context.Database.ExecuteSqlRawAsync("DELETE FROM mal_search_cache;", ct);
        }

        await transaction.CommitAsync(ct);
        await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(PASSIVE);", ct);
    }

    private static CacheCleanupStats GetDirectoryStats(CacheCleanupTarget target, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return new CacheCleanupStats(target, 0, 0);

        try
        {
            var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(x =>
                {
                    try { return new FileInfo(x); }
                    catch (Exception ex) { Log.Debug(ex, "CacheCleanupService: failed to get file info for {File}", x); return null; }
                })
                .Where(x => x is { Exists: true })
                .ToList();

            return new CacheCleanupStats(target, files.Count, files.Sum(x => x!.Length));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CacheCleanupService: failed to inspect {Path}", path);
            return new CacheCleanupStats(target, 0, 0);
        }
    }

    private static void DeleteDirectoryContents(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToList())
        {
            ct.ThrowIfCancellationRequested();
            try { File.Delete(file); }
            catch (Exception ex) { Log.Debug(ex, "CacheCleanupService: failed to delete {File}", file); }
        }

        foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                     .OrderByDescending(x => x.Length)
                     .ToList())
        {
            ct.ThrowIfCancellationRequested();
            try { Directory.Delete(dir, recursive: false); }
            catch (Exception ex) { Log.Debug(ex, "CacheCleanupService: failed to delete directory {Dir}", dir); }
        }
    }
}
