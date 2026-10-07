using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Utils;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public partial class AnimeSyncOrchestrator : IAnimeSyncOrchestrator
{


    private readonly IAnimeRepository _animeRepository;
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly IEnumerable<ITrackerService> _trackers;
    private readonly IRecognitionCache _recognitionCache;
    private readonly ISettingsService? _settingsService;

    private int _syncing;
    public bool IsSyncing => Volatile.Read(ref _syncing) == 1;

    public AnimeSyncOrchestrator(
        IAnimeRepository animeRepository,
        IUserAnimeRepository userAnimeRepo,
        IEnumerable<ITrackerService> trackers,
        IRecognitionCache recognitionCache,
        ISettingsService? settingsService = null)
    {
        _animeRepository = animeRepository;
        _userAnimeRepo = userAnimeRepo;
        _trackers = trackers;
        _recognitionCache = recognitionCache;
        _settingsService = settingsService;
    }

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
            status?.Report(TrackingLoc.GetLoc("sync.syncing.with", primaryTracker.Name));
            var apiList = await primaryTracker.GetUserAnimeListAsync(ct);
            if (apiList is null) return false;

            var currentItems = await _animeRepository.GetSnapshotAsync([MediaKind.Anime]);
            var localCount = currentItems.Count;
            if (!isMigration)
            {
                if (localCount >= 50 && apiList.Count < localCount * 0.7)
                {
                    Log.Warning("SyncWithTrackers: aborting - incoming list ({Incoming}) is much smaller than local cache ({Local}). Likely a partial fetch.",
                        apiList.Count, localCount);
                    return false;
                }

                if (!IsRemoteSnapshotSafe(currentItems, apiList))
                    return false;
            }

            await ProcessSyncResults(apiList, currentItems, status, ct);

            status?.Report("sync.saving.to_db");
            Log.Information("AnimeSyncOrchestrator: getting anime snapshot for DB sync");
            var snapshot = await _animeRepository.GetSnapshotAsync([MediaKind.Anime]);
            Log.Information("AnimeSyncOrchestrator: got {Count} items for DB sync", snapshot.Count);
            await _userAnimeRepo.SyncFromRemoteAsync(snapshot, [MediaKind.Anime], ct);

            Log.Information("AnimeSyncOrchestrator: getting full snapshot for recognition cache");
            var fullList = await _animeRepository.GetSnapshotAsync();
            Log.Information("AnimeSyncOrchestrator: building recognition cache for {Count} items", fullList.Count);
            await Task.Run(() => _recognitionCache.BuildIndex(fullList));
            Log.Information("AnimeSyncOrchestrator: recognition cache built");

            Log.Information("AnimeSyncOrchestrator: sending AnimeListRefreshMessage");
            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
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
            status?.Report(TrackingLoc.GetLoc("sync.syncing.with", primaryTracker.Name));
            var apiList = await primaryTracker.GetUserMangaListAsync(ct);
            if (apiList is null) return false;

            MediaKind[] kinds = [MediaKind.Manga, MediaKind.LightNovel];
            var currentItems = await _animeRepository.GetSnapshotAsync(kinds);
            var localCount = currentItems.Count;
            if (!isMigration)
            {
                if (localCount >= 50 && apiList.Count < localCount * 0.7)
                {
                    Log.Warning("SyncMangaWithTrackers: aborting - incoming list ({Incoming}) is much smaller than local cache ({Local}). Likely a partial fetch.",
                        apiList.Count, localCount);
                    return false;
                }

                if (!IsRemoteSnapshotSafe(currentItems, apiList))
                    return false;
            }

            await ProcessSyncResults(apiList, currentItems, status, ct);

            status?.Report("sync.saving.to_db");
            var snapshot = await _animeRepository.GetSnapshotAsync(kinds);
            await _userAnimeRepo.SyncFromRemoteAsync(snapshot, kinds, ct);

            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
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
}
