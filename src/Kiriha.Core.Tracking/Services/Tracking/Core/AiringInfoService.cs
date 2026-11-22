using System.Collections.Concurrent;
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
    private static readonly TimeSpan ReleaseRetry = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AniListPollInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan ReleaseWindow = TimeSpan.FromHours(2);
    private static readonly TimeSpan LateReleaseRetry = TimeSpan.FromHours(6);
    private static readonly TimeSpan UnknownScheduleRetry = TimeSpan.FromDays(1);
    private static readonly TimeSpan DistantScheduleRetry = TimeSpan.FromHours(12);
    private static readonly TimeSpan UpcomingWindow = TimeSpan.FromDays(1);
    private static readonly TimeSpan UpcomingScheduleRetry = TimeSpan.FromHours(2);
    private static readonly TimeSpan PreReleaseCheck = TimeSpan.FromMinutes(30);

    private readonly IAnimeRepository _animeRepo;
    private readonly IAnimeSyncOrchestrator _syncOrchestrator;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizer _localizer;
    private readonly Sync.LibrarySyncGate? _syncGate;
    private readonly AiringInfoFetcher _fetcher;
    private readonly IAniListApiService _aniListApi;
    private readonly AiringInfoCache _cache;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<MediaKey, (long Generation, EpisodeAiringSource Source, DateTime At)> _attempts = new();
    private long _nextAniListPollTicks;
    private int _aniListPollRunning;

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
        Sync.LibrarySyncGate? syncGate = null,
        TimeProvider? clock = null)
    {
        _animeRepo = animeRepo;
        _syncOrchestrator = syncOrchestrator;
        _uiDispatcher = uiDispatcher;
        _settingsService = settingsService;
        _localizer = localizer ?? ILocalizer.Passthrough;
        _syncGate = syncGate;
        _clock = clock ?? TimeProvider.System;
        _aniListApi = aniListApi;

        _fetcher = new AiringInfoFetcher(aniListApi, shikiApi, settingsService);
        _cache = new AiringInfoCache(animeRepo, userAnimeRepo, notificationService, uiDispatcher, syncGate);
    }

    private string FormatLoc(string key, params object?[] args) => _localizer.GetLoc(key, args);

    public static bool IsTrackableOngoingAnime(AnimeEntity anime, bool includeUnknownTotal = true)
    {
        if (anime.MediaKind != MediaKind.Anime) return false;

        var status = anime.StatusDetailed?.ToLowerInvariant();
        if (AppConstants.AiringStatus.IsFinishedAiring(status)) return false;
        if (status is "currently_airing" or "currently airing" or "ongoing" or "releasing"
            || AppConstants.AiringStatus.IsCurrentlyAiring(status)
            || anime.NextEpisodeAt.HasValue)
        {
            return true;
        }

        return includeUnknownTotal && (anime.TotalEpisodes <= 0 || anime.EpisodesAired < anime.TotalEpisodes);
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
            return;
        }

        var source = _settingsService.Current.System.AiringSource;
        var now = _clock.GetUtcNow().UtcDateTime;
        bool batchAniList = !force && source == EpisodeAiringSource.AniList;
        if (batchAniList && !IsAniListPollDue(now)) return;

        // Snapshot on UI thread - ObservableCollection is not thread-safe.
        var toSync = await _uiDispatcher.InvokeAsync(() =>
            _animeRepo.GetCollection()
                .Where(x =>
                    x.Status == UserAnimeStatus.Watching &&
                    IsTrackableOngoingAnime(x, includeUnknownTotal: false))
                .Select(anime => new AiringTarget(anime.Title, new MediaKey(anime.Id, anime.MediaKind),
                    _animeRepo.GetEntryGeneration(anime.Id, anime.MediaKind), anime.NextEpisodeAt, anime.LastEpisodesSync))
                .Where(entry => force || NeedsAutomaticCheck(entry.ExpectedAt,
                    GetLastCheck(entry.Key, entry.Generation, source, entry.LastSync), now, source))
                .ToList());

        if (toSync.Count == 0)
        {
            return;
        }

        Log.Information("AiringInfoService: Found {Count} anime to sync from {Source}.", toSync.Count, source);

        if (batchAniList)
        {
            await SyncAniListBatchAsync(toSync, progress, ct);
            return;
        }

        using var semaphore = new SemaphoreSlim(4);
        int completed = 0;
        var total = toSync.Count;

        var tasks = toSync.Select(async entry =>
        {
            var (title, key, generation, expectedAt, lastSync) = entry;
            if (ct.IsCancellationRequested) return;

            await semaphore.WaitAsync(ct);
            try
            {
                if (_syncOrchestrator.IsSyncing || (_syncGate?.IsFullSyncActive ?? false)) return;
                if (!force && !TryReserveCheck(key, generation, source, expectedAt, lastSync)) return;

                int currentCompleted = Interlocked.Increment(ref completed);
                var progressMsg = FormatLoc("sync.syncing.episodes_progress", currentCompleted.ToString(), total.ToString(), title);
                progress?.Report(progressMsg);

                Log.Information("AiringInfoService: Syncing {Source} airing info for {Title} (ID: {Id})...", source, title, key.Id);

                await FetchAndSaveAsync(key, generation, force || expectedAt <= now, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        Log.Information("AiringInfoService: {Source} sync cycle completed.", source);
    }

    private async Task SyncAniListBatchAsync(List<AiringTarget> targets, IProgress<string>? progress, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _aniListPollRunning, 1, 0) != 0) return;
        try
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            if (!IsAniListPollDue(now) || _syncOrchestrator.IsSyncing || (_syncGate?.IsFullSyncActive ?? false)) return;
            await PollAniListAsync(targets, progress, ct);
        }
        finally
        {
            Volatile.Write(ref _aniListPollRunning, 0);
        }
    }

    private bool IsAniListPollDue(DateTime now) => now.Ticks >= Volatile.Read(ref _nextAniListPollTicks);

    private async Task PollAniListAsync(List<AiringTarget> targets, IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const EpisodeAiringSource source = EpisodeAiringSource.AniList;
        var selected = targets.Where(entry => _animeRepo.GetEntryGeneration(entry.Key.Id, entry.Key.Kind) == entry.Generation &&
            TryReserveCheck(entry.Key, entry.Generation, source, entry.ExpectedAt, entry.LastSync)).ToList();
        if (selected.Count == 0) return;

        var requestedAt = _clock.GetUtcNow().UtcDateTime;
        Volatile.Write(ref _nextAniListPollTicks, (requestedAt + AniListPollInterval).Ticks);
        var results = await _aniListApi.GetAiringSchedulesAsync(selected.Select(entry => entry.Key.Id).ToArray(), ct);
        if (_settingsService.Current.System.AiringSource != source) return;

        int completed = 0;
        foreach (var entry in selected)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(FormatLoc("sync.syncing.episodes_progress", (++completed).ToString(), selected.Count.ToString(), entry.Title));
            if (results.TryGetValue(entry.Key.Id, out var airing))
                await SaveAsync(entry.Key, entry.Generation, airing, requestedAt, ct);
        }
    }

    private static bool NeedsAutomaticCheck(DateTime? expectedAt, DateTime? lastCheck, DateTime now, EpisodeAiringSource source)
    {
        if (expectedAt is not { } releaseAt)
            return lastCheck is null || now - lastCheck >= UnknownScheduleRetry;
        if (releaseAt > now)
        {
            if (source != EpisodeAiringSource.AniList) return false;
            var remaining = releaseAt - now;
            var preReleaseAt = releaseAt - PreReleaseCheck;
            var refreshInterval = remaining <= UpcomingWindow ? UpcomingScheduleRetry : DistantScheduleRetry;
            return lastCheck is null || now - lastCheck >= refreshInterval ||
                (now >= preReleaseAt && lastCheck < preReleaseAt);
        }

        var interval = now - releaseAt >= ReleaseWindow ? LateReleaseRetry :
            source == EpisodeAiringSource.AniList ? AniListPollInterval : ReleaseRetry;
        return lastCheck is null || lastCheck < releaseAt || now - lastCheck >= interval;
    }

    private bool TryReserveCheck(MediaKey key, long generation, EpisodeAiringSource source, DateTime? expectedAt, DateTime? lastSync)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var next = (generation, source, now);
        if (!_attempts.TryGetValue(key, out var previous))
            return _attempts.TryAdd(key, next);

        return NeedsAutomaticCheck(expectedAt, MostRecentCheck(previous, generation, source, lastSync), now, source) &&
            _attempts.TryUpdate(key, next, previous);
    }

    private DateTime? GetLastCheck(MediaKey key, long generation, EpisodeAiringSource source, DateTime? lastSync) =>
        _attempts.TryGetValue(key, out var previous) ? MostRecentCheck(previous, generation, source, lastSync) : lastSync;

    private static DateTime? MostRecentCheck((long Generation, EpisodeAiringSource Source, DateTime At) previous,
        long generation, EpisodeAiringSource source, DateTime? lastSync) =>
        previous.Generation == generation && previous.Source == source && previous.At > (lastSync ?? DateTime.MinValue)
            ? previous.At : lastSync;

    private async Task FetchAndSaveAsync(MediaKey key, long generation, bool force, CancellationToken ct)
    {
        if (_animeRepo.GetEntryGeneration(key.Id, key.Kind) != generation) return;

        // Do not hold either write gate during the provider request.
        var requestedAt = _clock.GetUtcNow().UtcDateTime;
        var airing = await _fetcher.FetchAsync(key.Id, force, ct);
        await SaveAsync(key, generation, airing, requestedAt, ct);
    }

    private async Task SaveAsync(MediaKey key, long generation, EpisodeAiringInfo? airing, DateTime requestedAt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (airing is null || airing.MalId != key.Id || airing.IsStaleFallback || _syncOrchestrator.IsSyncing) return;

        using var gateScope = _syncGate != null ? await _syncGate.TryAcquireBackgroundUpdateAsync(ct) : null;
        if (_syncGate != null && gateScope is null) return;

        await _cache.SaveAsync(key, generation, airing, requestedAt, ct);
    }

    private sealed record AiringTarget(string Title, MediaKey Key, long Generation, DateTime? ExpectedAt, DateTime? LastSync);
}
