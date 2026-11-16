using Kiriha.Core.Domain.Utils.Async;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public class AnimeListActionService : IAnimeListActionService
{
    private readonly IAnimeRepository _animeRepository;
    private readonly ISyncManager _syncManager;
    private readonly IProgressUpdateService _progressService;
    private readonly IHistoryService _historyService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ISettingsService? _settingsService;
    private readonly INotificationService? _notificationService;
    private readonly IEnumerable<ITrackerService> _trackers;
    private readonly IAiringInfoService? _airingInfoService;
    private readonly ILibraryMutationService _mutations;
    private readonly ICoreEventBus _eventBus;

    public AnimeListActionService(
        IAnimeRepository animeRepository,
        ISyncManager syncManager,
        IProgressUpdateService progressService,
        IHistoryService historyService,
        IUiDispatcher uiDispatcher,
        IEnumerable<ITrackerService> trackers,
        IUserAnimeRepository? userAnimeRepo = null,
        ISettingsService? settingsService = null,
        INotificationService? notificationService = null,
        IAiringInfoService? airingInfoService = null,
        ILibraryMutationService? mutations = null,
        ICoreEventBus? eventBus = null,
        TimeProvider? clock = null)
    {
        _animeRepository = animeRepository;
        _syncManager = syncManager;
        _progressService = progressService;
        _historyService = historyService;
        _uiDispatcher = uiDispatcher;
        _trackers = trackers;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _airingInfoService = airingInfoService;
        _eventBus = eventBus ?? CoreEventBus.Default;
        _mutations = mutations ?? new LibraryMutationService(
            animeRepository,
            userAnimeRepo,
            syncManager,
            uiDispatcher,
            _eventBus,
            clock);
    }

    public async Task<ListActionResult> AddToListAsync(
        AnimeEntity item,
        UserAnimeStatus status,
        int progress = 0,
        CancellationToken ct = default)
    {
        if (item == null || item.Id <= 0 || string.IsNullOrWhiteSpace(item.Title))
        {
            Log.Warning("AnimeListActionService.AddToListAsync: Rejected invalid anime (Id: {Id})", item?.Id);
            return ListActionResult.Fail("Invalid anime data");
        }

        if (status == UserAnimeStatus.None)
        {
            Log.Warning("AnimeListActionService.AddToListAsync: Cannot add anime {Id} with Status None", item.Id);
            return ListActionResult.Fail("Cannot add anime with status None");
        }

        if ((status == UserAnimeStatus.Watching || status == UserAnimeStatus.Completed) && AppConstants.AiringStatus.IsNotYetAired(item.StatusDetailed))
        {
            Log.Warning("AnimeListActionService: Cannot set {Title} to {Status} - it has not aired yet.", item.Title, status);
            return ListActionResult.Fail($"Cannot set '{item.Title}' to {status} because it has not aired yet.");
        }

        // Check if this anime already exists in local collection or DB to preserve user tracking data
        var existing = await _mutations.FindExistingAsync(item.Id, item.MediaKind, ct);
        if (existing != null)
        {
            item.PreserveUserFieldsFrom(existing);
            if (item.IsMissingDetails)
            {
                item.MergeFullDetails(existing);
            }
        }

        bool isManga = item.MediaKind != MediaKind.Anime;

        // Apply state changes to entity on UI thread
        await _uiDispatcher.InvokeAsync(() =>
        {
            item.Status = status;
            if (isManga)
            {
                if (progress > 0) item.ChaptersRead = progress;
                else if (item.ChaptersRead == 0 && existing != null && existing.ChaptersRead > 0) item.ChaptersRead = existing.ChaptersRead;
            }
            else
            {
                if (progress > 0) item.Progress = progress;
                else if (item.Progress == 0 && existing != null && existing.Progress > 0) item.Progress = existing.Progress;
            }

            _mutations.StampStatusDates(item, status, isStarting: status == UserAnimeStatus.Watching, existing);
            if (status == UserAnimeStatus.Completed)
            {
                if (!isManga && item.TotalEpisodes > 0 && item.Progress < item.TotalEpisodes)
                {
                    item.Progress = item.TotalEpisodes;
                }
                else if (isManga && item.Chapters > 0 && item.ChaptersRead < item.Chapters)
                {
                    item.ChaptersRead = item.Chapters;
                }
            }
        });

        // Save locally to Repository & DB
        await _animeRepository.AddOrUpdateAnimeAsync(item);

        // Record history entry
        int displayProgress = isManga ? item.ChaptersRead : item.Progress;
        var actionType = status switch
        {
            UserAnimeStatus.Completed => HistoryActionType.Completed,
            UserAnimeStatus.Dropped => HistoryActionType.Dropped,
            UserAnimeStatus.Watching => displayProgress == 0 ? HistoryActionType.AddedToList : HistoryActionType.Watched,
            UserAnimeStatus.PlanToWatch => HistoryActionType.PlanToWatch,
            UserAnimeStatus.OnHold => HistoryActionType.OnHold,
            _ => HistoryActionType.AddedToList
        };

        try
        {
            await _historyService.AddEntryAsync(
                item.Id,
                item.MediaKind,
                item.Title,
                item.RussianTitle,
                displayProgress,
                actionType,
                detail: null,
                posterUrl: item.MainPictureUrl,
                ct: ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AnimeListActionService: Failed to write history entry for {Title} ({Id})", item.Title, item.Id);
        }

        // Notify UI immediately (optimistic UI)
        _eventBus.Publish(new AnimeListRefreshMessage());

        // Check trackers and enqueue sync
        bool hasActiveTrackers = CheckHasActiveTrackers();
        if (hasActiveTrackers)
        {
            try
            {
                await _syncManager.EnqueueFullUpdateAsync(item);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AnimeListActionService: Failed to enqueue FullUpdate sync task for {Title} ({Id})", item.Title, item.Id);
            }
        }
        else
        {
            Log.Warning("AnimeListActionService: Added {Title} ({Id}) locally only. No active or logged-in trackers connected.", item.Title, item.Id);
            _notificationService?.NotifySyncFailed(
                item.Title,
                "Trackers",
                "Added locally: no active tracker accounts connected",
                willRetry: false);
        }

        // Trigger immediate airing info & timer sync if anime is ongoing
        TriggerAiringSyncIfOngoing(item);

        return ListActionResult.Ok(
            hasActiveTrackers: hasActiveTrackers,
            message: hasActiveTrackers ? null : "Added locally: no active tracker accounts connected");
    }

    public async Task<ListActionResult> SaveAnimeAsync(
        AnimeEntity originalItem,
        AnimeEntity updatedItem,
        CancellationToken ct = default)
    {
        if (originalItem == null || originalItem.Id <= 0)
        {
            return ListActionResult.Fail("Invalid original anime");
        }

        if (updatedItem == null || updatedItem.Id <= 0)
        {
            return ListActionResult.Fail("Invalid updated anime");
        }

        if (originalItem.Id != updatedItem.Id)
        {
            return ListActionResult.Fail("Mismatched anime IDs");
        }

        if (updatedItem.Status == UserAnimeStatus.None)
        {
            return await RemoveFromListAsync(originalItem.Id, originalItem.MediaKind, ct);
        }

        bool isManga = originalItem.MediaKind != MediaKind.Anime;
        int oldProgress = isManga ? originalItem.ChaptersRead : originalItem.Progress;
        int newProgress = isManga ? updatedItem.ChaptersRead : updatedItem.Progress;
        bool progressChanged = oldProgress != newProgress;

        bool markedAsDropped = originalItem.Status != UserAnimeStatus.Dropped && updatedItem.Status == UserAnimeStatus.Dropped;
        bool markedAsCompleted = originalItem.Status != UserAnimeStatus.Completed && updatedItem.Status == UserAnimeStatus.Completed;
        bool statusChanged = originalItem.Status != updatedItem.Status;

        if (statusChanged &&
            (updatedItem.Status == UserAnimeStatus.Watching || updatedItem.Status == UserAnimeStatus.Completed) &&
            AppConstants.AiringStatus.IsNotYetAired(updatedItem.StatusDetailed))
        {
            Log.Warning("AnimeListActionService: Cannot set {Title} to {Status} - it has not aired yet.", updatedItem.Title, updatedItem.Status);
            return ListActionResult.Fail($"Cannot set '{updatedItem.Title}' to {updatedItem.Status} because it has not aired yet.");
        }

        bool scoreChanged = originalItem.UserScore != updatedItem.UserScore && updatedItem.UserScore is > 0;

        await _uiDispatcher.InvokeAsync(() =>
        {
            _mutations.StampStatusDates(
                updatedItem,
                markedAsCompleted ? UserAnimeStatus.Completed : null,
                isStarting: updatedItem.Status == UserAnimeStatus.Watching);

            updatedItem.CopyTo(originalItem);
            if (progressChanged)
            {
                _eventBus.Publish(new AnimeProgressChangedMessage(originalItem, newProgress));
            }
        });

        await _animeRepository.AddOrUpdateAnimeAsync(originalItem);

        int displayProgress = newProgress;

        try
        {
            if (markedAsCompleted)
            {
                await _historyService.AddEntryAsync(originalItem.Id, originalItem.MediaKind, originalItem.Title, originalItem.RussianTitle, displayProgress, HistoryActionType.Completed, null, originalItem.MainPictureUrl, ct);
            }
            else
            {
                if (progressChanged)
                {
                    var progressAction = newProgress > oldProgress
                        ? HistoryActionType.Watched
                        : HistoryActionType.Reverted;
                    await _historyService.AddEntryAsync(originalItem.Id, originalItem.MediaKind, originalItem.Title, originalItem.RussianTitle, displayProgress, progressAction, null, originalItem.MainPictureUrl, ct);
                }

                if (markedAsDropped)
                {
                    await _historyService.AddEntryAsync(originalItem.Id, originalItem.MediaKind, originalItem.Title, originalItem.RussianTitle, displayProgress, HistoryActionType.Dropped, null, originalItem.MainPictureUrl, ct);
                }
                else if (statusChanged)
                {
                    bool alreadyRecordedProgress = progressChanged && newProgress > oldProgress && updatedItem.Status == UserAnimeStatus.Watching;
                    if (!alreadyRecordedProgress)
                    {
                        var statusAction = updatedItem.Status switch
                        {
                            UserAnimeStatus.Watching => displayProgress == 0 ? HistoryActionType.AddedToList : HistoryActionType.Watched,
                            UserAnimeStatus.PlanToWatch => HistoryActionType.PlanToWatch,
                            UserAnimeStatus.OnHold => HistoryActionType.OnHold,
                            _ => HistoryActionType.AddedToList
                        };

                        await _historyService.AddEntryAsync(originalItem.Id, originalItem.MediaKind, originalItem.Title, originalItem.RussianTitle, displayProgress, statusAction, null, originalItem.MainPictureUrl, ct);
                    }
                }
            }

            if (scoreChanged)
            {
                await _historyService.AddEntryAsync(originalItem.Id, originalItem.MediaKind, originalItem.Title, originalItem.RussianTitle, displayProgress, HistoryActionType.ScoreSet, originalItem.Score, originalItem.MainPictureUrl, ct);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AnimeListActionService: Failed to record history during SaveAnimeAsync for {Id}", originalItem.Id);
        }

        _eventBus.Publish(new AnimeListRefreshMessage());

        bool hasActiveTrackers = CheckHasActiveTrackers();
        if (hasActiveTrackers)
        {
            await _syncManager.EnqueueFullUpdateAsync(originalItem);
        }
        else
        {
            Log.Warning("AnimeListActionService: Saved {Title} ({Id}) locally only. No active trackers connected.", originalItem.Title, originalItem.Id);
            _notificationService?.NotifySyncFailed(
                originalItem.Title,
                "Trackers",
                "Saved locally: no active tracker accounts connected",
                willRetry: false);
        }

        if (originalItem.Status == UserAnimeStatus.Watching)
        {
            TriggerAiringSyncIfOngoing(originalItem);
        }

        return ListActionResult.Ok(
            hasActiveTrackers: hasActiveTrackers,
            message: hasActiveTrackers ? null : "Saved locally: no active tracker accounts connected");
    }

    private void TriggerAiringSyncIfOngoing(AnimeEntity item)
    {
        if (_airingInfoService == null || item.Status != UserAnimeStatus.Watching) return;
        if (!Core.AiringInfoService.IsTrackableOngoingAnime(item, includeUnknownTotal: true)) return;

        Kiriha.Core.Domain.Utils.Async.TaskExtensions.SafeFireAndForget(
            _airingInfoService.SyncEpisodesForAnimeAsync(item),
            "AnimeListActionService.TriggerAiringSync");
    }

    public Task<ListActionResult> RemoveFromListAsync(
        int animeId,
        CancellationToken ct = default) =>
        RemoveFromListAsync(animeId, MediaKind.Anime, ct);

    public async Task<ListActionResult> RemoveFromListAsync(
        int animeId,
        MediaKind mediaKind,
        CancellationToken ct = default)
    {
        if (animeId <= 0) return ListActionResult.Fail("Invalid anime ID");

        if (mediaKind == MediaKind.Anime)
            await _progressService.RemoveAnimeAsync(animeId);
        else
            await _progressService.RemoveAnimeAsync(animeId, mediaKind);

        _eventBus.Publish(new AnimeListRefreshMessage());

        bool hasActiveTrackers = CheckHasActiveTrackers();
        return ListActionResult.Ok(hasActiveTrackers);
    }

    private bool CheckHasActiveTrackers()
    {
        var active = _trackers.Where(t => t.IsEnabled).ToList();
        if (_settingsService == null) return active.Count > 0;

        var api = _settingsService.Current.Api;
        var primary = api.GetPrimaryAccount();
        var mirrors = api.GetMirrorAccounts().ToList();

        var destinations = active.Where(tracker =>
        {
            if (primary != null && string.Equals(tracker.TrackerId, primary.TrackerId, StringComparison.OrdinalIgnoreCase))
                return true;

            return mirrors.Any(m => string.Equals(tracker.TrackerId, m.TrackerId, StringComparison.OrdinalIgnoreCase));
        }).ToList();

        return destinations.Count > 0;
    }
}
