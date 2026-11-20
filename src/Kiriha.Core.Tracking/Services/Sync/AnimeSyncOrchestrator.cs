using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public partial class AnimeSyncOrchestrator : IAnimeSyncOrchestrator
{
    private readonly IAnimeRepository _animeRepository;
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly IEnumerable<ITrackerService> _trackers;
    private readonly IRecognitionCache _recognitionCache;
    private readonly ISettingsService? _settingsService;
    private readonly ILocalizer _localizer;
    private readonly LibrarySyncGate? _syncGate;
    private readonly ISyncTaskRepository? _syncTaskRepo;
    private readonly ICoreEventBus _eventBus;

    private int _syncing;
    public bool IsSyncing => Volatile.Read(ref _syncing) == 1;

    public AnimeSyncOrchestrator(
        IAnimeRepository animeRepository,
        IUserAnimeRepository userAnimeRepo,
        IEnumerable<ITrackerService> trackers,
        IRecognitionCache recognitionCache,
        ISettingsService? settingsService = null,
        ILocalizer? localizer = null,
        LibrarySyncGate? syncGate = null,
        ISyncTaskRepository? syncTaskRepo = null,
        ICoreEventBus? eventBus = null)
    {
        _animeRepository = animeRepository;
        _userAnimeRepo = userAnimeRepo;
        _trackers = trackers;
        _recognitionCache = recognitionCache;
        _settingsService = settingsService;
        _localizer = localizer ?? ILocalizer.Passthrough;
        _syncGate = syncGate;
        _syncTaskRepo = syncTaskRepo;
        _eventBus = eventBus ?? CoreEventBus.Default;
    }

    private string FormatLoc(string key, params object?[] args) => _localizer.GetLoc(key, args);

    private ITrackerService? GetPrimaryTracker()
    {
        if (_settingsService != null)
        {
            var primaryAccount = _settingsService.Current.Api.GetPrimaryAccount();
            if (primaryAccount != null)
            {
                var matched = _trackers.FirstOrDefault(t =>
                    string.Equals(t.TrackerId, primaryAccount.TrackerId, StringComparison.OrdinalIgnoreCase) && t.IsEnabled);
                if (matched != null) return matched;
            }
        }

        return _trackers.FirstOrDefault(t => t.IsEnabled);
    }

    public async Task<bool> SyncWithTrackersAsync(IProgress<string>? status = null, CancellationToken ct = default, bool isMigration = false)
    {
        if (Interlocked.CompareExchange(ref _syncing, 1, 0) != 0) return false;

        var primaryTracker = GetPrimaryTracker();
        if (primaryTracker is null)
        {
            Log.Warning("No active trackers found for synchronization.");
            Interlocked.Exchange(ref _syncing, 0);
            return false;
        }

        try
        {
            using var gateScope = _syncGate != null ? await _syncGate.AcquireFullSyncAsync(ct) : null;
            var pendingAtFetchStart = await GetPendingTasksAsync(ct);
            status?.Report(FormatLoc("sync.syncing.with", primaryTracker.Name));
            var apiList = await primaryTracker.GetUserAnimeListAsync(ct);
            if (apiList is null) return false;

            var snapshot = await ApplyTrackerListAsync(apiList, [MediaKind.Anime], pendingAtFetchStart, isMigration, status, ct);
            if (snapshot is null) return false;

            Log.Information("AnimeSyncOrchestrator: building recognition cache for {Count} anime items", snapshot.Count);
            await Task.Run(() => _recognitionCache.BuildIndex(snapshot), ct);
            Log.Information("AnimeSyncOrchestrator: recognition cache built");

            Log.Information("AnimeSyncOrchestrator: publishing AnimeListRefreshMessage");
            _eventBus.Publish(new AnimeListRefreshMessage());
            Log.Information("AnimeSyncOrchestrator: anime sync completed successfully");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Failed to sync with {Tracker}", primaryTracker.Name);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _syncing, 0);
        }
    }

    public async Task<bool> SyncMangaWithTrackersAsync(IProgress<string>? status = null, CancellationToken ct = default, bool isMigration = false)
    {
        if (Interlocked.CompareExchange(ref _syncing, 1, 0) != 0) return false;

        var primaryTracker = GetPrimaryTracker();
        if (primaryTracker is null)
        {
            Log.Warning("No active trackers found for synchronization.");
            Interlocked.Exchange(ref _syncing, 0);
            return false;
        }

        try
        {
            using var gateScope = _syncGate != null ? await _syncGate.AcquireFullSyncAsync(ct) : null;
            var pendingAtFetchStart = await GetPendingTasksAsync(ct);
            status?.Report(FormatLoc("sync.syncing.with", primaryTracker.Name));
            var apiList = await primaryTracker.GetUserMangaListAsync(ct);
            if (apiList is null) return false;

            var snapshot = await ApplyTrackerListAsync(
                apiList, [MediaKind.Manga, MediaKind.LightNovel], pendingAtFetchStart, isMigration, status, ct);
            if (snapshot is null) return false;

            _eventBus.Publish(new AnimeListRefreshMessage());
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Failed to sync manga with {Tracker}", primaryTracker.Name);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _syncing, 0);
        }
    }

    private async Task<List<SyncTaskEntity>> GetPendingTasksAsync(CancellationToken ct) =>
        _syncTaskRepo != null ? await _syncTaskRepo.GetPendingAsync(ct) : [];

    private async Task<List<AnimeEntity>?> ApplyTrackerListAsync(
        List<AnimeEntity> apiList, MediaKind[] kinds, List<SyncTaskEntity> pendingAtFetchStart,
        bool isMigration, IProgress<string>? status, CancellationToken ct)
    {
        // Local progress can be saved during the request. Serialize reconciliation and its
        // SQLite write with progress commits once the response arrives.
        using var progressScope = _syncGate != null ? await _syncGate.AcquireProgressWriteAsync(ct) : null;
        var currentItems = await _animeRepository.GetSnapshotAsync(kinds);
        if (!isMigration)
        {
            var localCount = currentItems.Count;
            if (localCount >= SyncSafetyConstants.MinimumTotalGuardCount &&
                (double)(localCount - apiList.Count) / localCount > SyncSafetyConstants.MaximumAllowedTotalDropRatio)
            {
                Log.Warning("Full list sync: incoming list ({Incoming}) is much smaller than local cache ({Local}). Likely a partial fetch.",
                    apiList.Count, localCount);
                return null;
            }

            if (!IsRemoteSnapshotSafe(currentItems, apiList))
                return null;
        }

        var pendingTasks = await GetPendingTasksAsync(ct);
        var protectedItems = _syncGate?.GetProgressChangesDuringSync() ?? [];
        await ProcessSyncResults(apiList, currentItems, pendingTasks, pendingAtFetchStart, protectedItems, status, ct);

        status?.Report(FormatLoc("sync.saving.to_db"));
        var snapshot = await _animeRepository.GetSnapshotAsync(kinds);
        await _userAnimeRepo.SyncFromRemoteAsync(snapshot, kinds, ct);
        return snapshot;
    }
}
