using System;
using Kiriha.Core;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Infrastructure;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Core;

public class AiringInfoService : Kiriha.Core.Abstractions.Services.IAiringInfoService
{
    private readonly IAnimeRepository _animeRepo;
    private readonly IAnimeSyncOrchestrator _syncOrchestrator;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly AiringInfoFetcher _fetcher;
    private readonly AiringInfoCache _cache;

    public AiringInfoService(
        IAniListApiService aniListApi,
        IAnimeRepository animeRepo,
        IAnimeSyncOrchestrator syncOrchestrator,
        INotificationService notificationService,
        IUiDispatcher uiDispatcher)
    {
        _animeRepo = animeRepo;
        _syncOrchestrator = syncOrchestrator;
        _uiDispatcher = uiDispatcher;

        _fetcher = new AiringInfoFetcher(aniListApi);
        _cache = new AiringInfoCache(animeRepo, notificationService, uiDispatcher);
    }

    public async Task SyncEpisodesForAnimeAsync(AnimeEntity anime, CancellationToken ct = default)
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
            await _cache.MarkSyncedAsync(anime, DateTime.UtcNow);
            return;
        }

        await _cache.ApplyAndSaveAiringAsync(anime, aired, nextSlot, DateTime.UtcNow);
    }

    public async Task SyncOngoingEpisodesAsync(bool force = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (_syncOrchestrator.IsSyncing)
        {
            Log.Information("AiringInfoService: Main sync is in progress, skipping episode sync to avoid DB conflicts.");
            return;
        }

        Log.Information("AiringInfoService: Checking AniList airing info (Force: {Force})...", force);

        var threshold = DateTime.UtcNow.AddHours(-6);
        // Snapshot on UI thread - ObservableCollection is not thread-safe.
        var toSync = await _uiDispatcher.InvokeAsync(() =>
            _animeRepo.GetCollection()
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

        var semaphore = new SemaphoreSlim(4);
        int completed = 0;
        var total = toSync.Count;

        var tasks = toSync.Select(async (anime, i) =>
        {
            if (ct.IsCancellationRequested) return;
            if (_animeRepo.IsRecentlyDeleted(anime.Id)) return;

            await semaphore.WaitAsync(ct);
            try
            {
                int currentCompleted = Interlocked.Increment(ref completed);
                var progressMsg = UIUtils.GetLoc("sync.syncing.episodes_progress", currentCompleted.ToString(), total.ToString(), anime.Title);
                progress?.Report(progressMsg);

                Log.Information("AiringInfoService: Syncing AniList airing info for {Title} (ID: {Id})...", anime.Title, anime.Id);

                var now = DateTime.UtcNow;
                var (airing, aired, nextSlot) = await _fetcher.FetchAndResolveAsync(anime, force, ct);
                if (airing == null)
                {
                    await _cache.MarkSyncedAsync(anime, now);
                    return;
                }

                await _cache.ApplyAndSaveAiringAsync(anime, aired, nextSlot, now);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        Log.Information("AiringInfoService: AniList sync cycle completed.");
    }
}
