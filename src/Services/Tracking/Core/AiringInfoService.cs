using Kiriha.Services.Data.Repository;
using Kiriha.Services.Data.Sync;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Serilog;

namespace Kiriha.Services.Tracking.Core;

public class AiringInfoService
{
    private readonly AnimeRepository _animeRepo;
    private readonly AnimeSyncOrchestrator _syncOrchestrator;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly AiringInfoFetcher _fetcher;
    private readonly AiringInfoCache _cache;

    public AiringInfoService(
        AniListApiService aniListApi,
        AnimeRepository animeRepo,
        AnimeSyncOrchestrator syncOrchestrator,
        NotificationService notificationService,
        IUiDispatcher uiDispatcher)
    {
        _animeRepo = animeRepo;
        _syncOrchestrator = syncOrchestrator;
        _uiDispatcher = uiDispatcher;
        
        _fetcher = new AiringInfoFetcher(aniListApi);
        _cache = new AiringInfoCache(animeRepo, notificationService, uiDispatcher);
    }

    public async Task SyncEpisodesForAnimeAsync(AnimeItem anime, CancellationToken ct = default)
    {
        if (_syncOrchestrator.IsSyncing) return;
        if (anime.Status != UserAnimeStatus.Watching) return;

        var status = anime.StatusDetailed?.ToLowerInvariant();
        bool isTrackableStatus = status == "currently_airing" || status == "currently airing";

        if (!isTrackableStatus && !anime.NextEpisodeAt.HasValue) return;

        Log.Information("AiringInfoService: Immediate AniList sync requested for {Title} (ID: {Id})", anime.Title, anime.Id);

        var (airing, aired, nextSlot) = await _fetcher.FetchAndResolveAsync(anime, force: true, ct);
        if (_animeRepo.IsRecentlyDeleted(anime.Id)) return;

        if (airing == null)
        {
            await _cache.MarkSyncedAsync(anime, DateTime.Now);
            return;
        }

        await _cache.ApplyAndSaveAiringAsync(anime, aired, nextSlot, DateTime.Now);
    }

    public async Task SyncOngoingEpisodesAsync(bool force = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (_syncOrchestrator.IsSyncing)
        {
            Log.Information("AiringInfoService: Main sync is in progress, skipping episode sync to avoid DB conflicts.");
            return;
        }

        Log.Information("AiringInfoService: Checking AniList airing info (Force: {Force})...", force);

        var threshold = DateTime.Now.AddHours(-6);
        // Snapshot on UI thread - ObservableCollection is not thread-safe.
        var toSync = await _uiDispatcher.InvokeAsync(() =>
            _animeRepo.Collection
                .Where(x =>
                {
                    var s = x.StatusDetailed?.ToLowerInvariant();
                    return (s == "currently_airing" || s == "currently airing" || x.NextEpisodeAt.HasValue) &&
                           x.Status == UserAnimeStatus.Watching &&
                           (force || x.LastEpisodesSync == null || x.LastEpisodesSync < threshold);
                })
                .ToList());

        if (!toSync.Any())
        {
            Log.Information("AiringInfoService: No anime needs syncing at this time.");
            return;
        }

        Log.Information("AiringInfoService: Found {Count} anime to sync from AniList.", toSync.Count);

        for (int i = 0; i < toSync.Count; i++)
        {
            var anime = toSync[i];
            if (ct.IsCancellationRequested) break;
            if (_animeRepo.IsRecentlyDeleted(anime.Id)) continue;

            var progressMsg = UIUtils.GetLoc("sync.syncing.episodes_progress", (i + 1).ToString(), toSync.Count.ToString(), anime.Title);
            progress?.Report(progressMsg);

            Log.Information("AiringInfoService: Syncing AniList airing info for {Title} (ID: {Id})...", anime.Title, anime.Id);

            var now = DateTime.Now;
            var (airing, aired, nextSlot) = await _fetcher.FetchAndResolveAsync(anime, force, ct);
            if (airing == null)
            {
                await _cache.MarkSyncedAsync(anime, now);
                continue;
            }

            await _cache.ApplyAndSaveAiringAsync(anime, aired, nextSlot, now);
        }

        Log.Information("AiringInfoService: AniList sync cycle completed.");
    }
}
