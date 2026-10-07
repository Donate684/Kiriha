using Kiriha.Core.Abstractions.Services;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public class AnimeRefreshService : IAnimeRefreshService
{
    private readonly IAnimeSyncOrchestrator _syncOrchestrator;
    private readonly IAiringInfoService _airingInfoService;

    public AnimeRefreshService(
        IAnimeSyncOrchestrator syncOrchestrator,
        IAiringInfoService airingInfoService)
    {
        _syncOrchestrator = syncOrchestrator;
        _airingInfoService = airingInfoService;
    }

    public bool IsRefreshing => _syncOrchestrator.IsSyncing;

    public async Task<bool> RefreshAnimeListAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        bool isMigration = false)
    {
        Log.Information("AnimeRefreshService: Starting full anime refresh cycle...");
        var trackerSuccess = await _syncOrchestrator.SyncWithTrackersAsync(progress, ct, isMigration);
        if (!trackerSuccess)
        {
            Log.Warning("AnimeRefreshService: Tracker sync failed or aborted; skipping airing info sync.");
            return false;
        }

        try
        {
            await _airingInfoService.SyncOngoingEpisodesAsync(force: true, progress, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "AnimeRefreshService: Airing info sync failed, tracker sync completed successfully.");
        }

        Log.Information("AnimeRefreshService: Full anime refresh cycle completed.");
        return true;
    }

    public async Task<bool> RefreshMangaListAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        bool isMigration = false)
    {
        return await _syncOrchestrator.SyncMangaWithTrackersAsync(progress, ct, isMigration);
    }
}
