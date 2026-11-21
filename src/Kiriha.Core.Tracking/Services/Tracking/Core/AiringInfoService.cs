using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Core;

public class AiringInfoService : IAiringInfoService
{
    private readonly IAnimeRepository _animeRepo;
    private readonly IAnimeSyncOrchestrator _syncOrchestrator;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizer _localizer;
    private readonly Sync.LibrarySyncGate? _syncGate;
    private readonly AiringInfoFetcher _fetcher;
    private readonly AiringInfoCache _cache;

    public AiringInfoService(
        IAniListApiService aniListApi,
        IShikiApiService shikiApi,
        ISettingsService settingsService,
        IAnimeRepository animeRepo,
        IUserAnimeRepository userAnimeRepo,
        IAnimeSyncOrchestrator syncOrchestrator,
        INotificationService notificationService,
        IUiDispatcher uiDispatcher,
        ILocalizer? localizer = null,
        Sync.LibrarySyncGate? syncGate = null)
    {
        _animeRepo = animeRepo;
        _syncOrchestrator = syncOrchestrator;
        _uiDispatcher = uiDispatcher;
        _settingsService = settingsService;
        _localizer = localizer ?? ILocalizer.Passthrough;
        _syncGate = syncGate;

        _fetcher = new AiringInfoFetcher(aniListApi, shikiApi, settingsService);
        _cache = new AiringInfoCache(animeRepo, userAnimeRepo, notificationService, uiDispatcher, syncGate);
    }

    private string FormatLoc(string key, params object?[] args) => _localizer.GetLoc(key, args);

    public static bool IsTrackableOngoingAnime(AnimeEntity anime, bool includeUnknownTotal = true)
    {
        if (anime.MediaKind != MediaKind.Anime) return false;

        var status = anime.StatusDetailed?.ToLowerInvariant();
        if (status is "currently_airing" or "currently airing" or "ongoing" or "releasing"
            || AppConstants.AiringStatus.IsCurrentlyAiring(status)
            || anime.NextEpisodeAt.HasValue)
        {
            return true;
        }

        return includeUnknownTotal &&
               (anime.TotalEpisodes <= 0 ||
                (!AppConstants.AiringStatus.IsFinishedAiring(status) && anime.EpisodesAired < anime.TotalEpisodes));
    }

    public async Task SyncEpisodesForAnimeAsync(AnimeEntity anime, CancellationToken ct = default)
    {
        if (_syncOrchestrator.IsSyncing || (_syncGate?.IsFullSyncActive ?? false)) return;
        var key = new MediaKey(anime.Id, anime.MediaKind);
        var generation = _animeRepo.GetEntryGeneration(key.Id, key.Kind);
        if (_animeRepo.FindById(key.Id, key.Kind) is not { Status: UserAnimeStatus.Watching } current ||
            !IsTrackableOngoingAnime(current, includeUnknownTotal: true)) return;

        var source = _settingsService.Current.System.AiringSource;
        Log.Information("AiringInfoService: Immediate {Source} sync requested for {Title} (ID: {Id})", source, anime.Title, anime.Id);

        await FetchAndSaveAsync(key, generation, force: true, ct);
    }

    public async Task SyncOngoingEpisodesAsync(bool force = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (_syncOrchestrator.IsSyncing || (_syncGate?.IsFullSyncActive ?? false))
        {
            Log.Information("AiringInfoService: Main sync is in progress, skipping episode sync to avoid DB conflicts.");
            return;
        }

        var source = _settingsService.Current.System.AiringSource;
        Log.Information("AiringInfoService: Checking {Source} airing info (Force: {Force})...", source, force);

        var threshold = DateTime.UtcNow.AddHours(-6);
        // Snapshot on UI thread - ObservableCollection is not thread-safe.
        var toSync = await _uiDispatcher.InvokeAsync(() =>
            _animeRepo.GetCollection()
                .Where(x =>
                    x.Status == UserAnimeStatus.Watching &&
                    IsTrackableOngoingAnime(x, includeUnknownTotal: false) &&
                    (force || x.LastEpisodesSync is null || x.LastEpisodesSync < threshold))
                .Select(anime => (Anime: anime, Generation: _animeRepo.GetEntryGeneration(anime.Id, anime.MediaKind)))
                .ToList());

        if (toSync.Count == 0)
        {
            Log.Information("AiringInfoService: No anime needs syncing at this time.");
            return;
        }

        Log.Information("AiringInfoService: Found {Count} anime to sync from {Source}.", toSync.Count, source);

        using var semaphore = new SemaphoreSlim(4);
        int completed = 0;
        var total = toSync.Count;

        var tasks = toSync.Select(async entry =>
        {
            var (anime, generation) = entry;
            if (ct.IsCancellationRequested) return;

            await semaphore.WaitAsync(ct);
            try
            {
                if (_syncOrchestrator.IsSyncing || (_syncGate?.IsFullSyncActive ?? false)) return;

                int currentCompleted = Interlocked.Increment(ref completed);
                var progressMsg = FormatLoc("sync.syncing.episodes_progress", currentCompleted.ToString(), total.ToString(), anime.Title);
                progress?.Report(progressMsg);

                Log.Information("AiringInfoService: Syncing {Source} airing info for {Title} (ID: {Id})...", source, anime.Title, anime.Id);

                await FetchAndSaveAsync(new MediaKey(anime.Id, anime.MediaKind), generation, force, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        Log.Information("AiringInfoService: {Source} sync cycle completed.", source);
    }

    private async Task FetchAndSaveAsync(MediaKey key, long generation, bool force, CancellationToken ct)
    {
        if (_animeRepo.GetEntryGeneration(key.Id, key.Kind) != generation) return;

        // Do not hold either write gate during the provider request.
        var requestedAt = DateTime.UtcNow;
        var airing = await _fetcher.FetchAsync(key.Id, force, ct);
        ct.ThrowIfCancellationRequested();
        if (airing is null || airing.IsStaleFallback || _syncOrchestrator.IsSyncing) return;

        using var gateScope = _syncGate != null ? await _syncGate.TryAcquireBackgroundUpdateAsync(ct) : null;
        if (_syncGate != null && gateScope is null) return;

        await _cache.SaveAsync(key, generation, airing, requestedAt, ct);
    }
}
