using Kiriha.Services.Data.Sync;
using Kiriha.Services.Data.Mapping;
using Kiriha.Core.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Services.Data.Repository;
using Serilog;

namespace Kiriha.Services.Data.Sync;

public partial class AnimeSyncOrchestrator
{


    private readonly AnimeRepository _animeRepository;
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly IEnumerable<ITrackerService> _trackers;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly RecognitionCache _recognitionCache;

    private int _syncing;
    public bool IsSyncing => Volatile.Read(ref _syncing) == 1;

    public AnimeSyncOrchestrator(
        AnimeRepository animeRepository,
        IUserAnimeRepository userAnimeRepo,
        IEnumerable<ITrackerService> trackers,
        IUiDispatcher uiDispatcher,
        RecognitionCache recognitionCache)
    {
        _animeRepository = animeRepository;
        _userAnimeRepo = userAnimeRepo;
        _trackers = trackers;
        _uiDispatcher = uiDispatcher;
        _recognitionCache = recognitionCache;
    }

    public async Task<bool> SyncWithTrackersAsync(IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _syncing, 1, 0) != 0) return false;

        var primaryTracker = _trackers.FirstOrDefault(t => t.IsEnabled);
        if (primaryTracker == null)
        {
            Log.Warning("No active trackers found for synchronization.");
            Interlocked.Exchange(ref _syncing, 0);
            return false;
        }

        try
        {
            status?.Report(UIUtils.GetLoc("sync.syncing.with", primaryTracker.Name));
            var apiList = await primaryTracker.GetUserAnimeListAsync(ct);
            if (apiList == null) return false;

            var currentItems = await _animeRepository.GetSnapshotAsync(new[] { MediaKind.Anime });
            var localCount = currentItems.Count;
            if (localCount >= 50 && apiList.Count < localCount * 0.7)
            {
                Log.Warning("SyncWithTrackers: aborting - incoming list ({Incoming}) is much smaller than local cache ({Local}). Likely a partial fetch.",
                    apiList.Count, localCount);
                return false;
            }

            if (!IsRemoteSnapshotSafe(currentItems, apiList))
                return false;

            await ProcessSyncResults(apiList, currentItems, status, ct);

            status?.Report(UIUtils.GetLoc("sync.saving.to_db"));
            var snapshot = await _animeRepository.GetSnapshotAsync(new[] { MediaKind.Anime });
            await _userAnimeRepo.SyncFromRemoteAsync(snapshot, new[] { MediaKind.Anime });

            await Task.Run(() => _recognitionCache.BuildIndex(_animeRepository.Collection));

            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
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

    public async Task<bool> SyncMangaWithTrackersAsync(IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _syncing, 1, 0) != 0) return false;

        var primaryTracker = _trackers.FirstOrDefault(t => t.IsEnabled);
        if (primaryTracker == null)
        {
            Log.Warning("No active trackers found for synchronization.");
            Interlocked.Exchange(ref _syncing, 0);
            return false;
        }

        try
        {
            status?.Report(UIUtils.GetLoc("sync.syncing.with", primaryTracker.Name));
            var apiList = await primaryTracker.GetUserMangaListAsync(ct);
            if (apiList == null) return false;

            var kinds = new[] { MediaKind.Manga, MediaKind.LightNovel };
            var currentItems = await _animeRepository.GetSnapshotAsync(kinds);
            var localCount = currentItems.Count;
            if (localCount >= 50 && apiList.Count < localCount * 0.7)
            {
                Log.Warning("SyncMangaWithTrackers: aborting - incoming list ({Incoming}) is much smaller than local cache ({Local}). Likely a partial fetch.",
                    apiList.Count, localCount);
                return false;
            }

            if (!IsRemoteSnapshotSafe(currentItems, apiList))
                return false;

            await ProcessSyncResults(apiList, currentItems, status, ct);

            status?.Report(UIUtils.GetLoc("sync.saving.to_db"));
            var snapshot = await _animeRepository.GetSnapshotAsync(kinds);
            await _userAnimeRepo.SyncFromRemoteAsync(snapshot, kinds);

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
