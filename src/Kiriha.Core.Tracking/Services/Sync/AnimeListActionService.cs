using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Infrastructure;
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
    private readonly IUserAnimeRepository? _userAnimeRepo;
    private readonly ISettingsService? _settingsService;
    private readonly INotificationService? _notificationService;
    private readonly IEnumerable<ITrackerService> _trackers;
    private readonly IAiringInfoService? _airingInfoService;

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
        IAiringInfoService? airingInfoService = null)
    {
        _animeRepository = animeRepository;
        _syncManager = syncManager;
        _progressService = progressService;
        _historyService = historyService;
        _uiDispatcher = uiDispatcher;
        _trackers = trackers;
        _userAnimeRepo = userAnimeRepo;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _airingInfoService = airingInfoService;
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
        var existing = _animeRepository.Collection?.FirstOrDefault(x => x.Id == item.Id)
                       ?? (_userAnimeRepo != null ? await _userAnimeRepo.GetByIdAsync(item.Id, ct) : null);
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

            if (status == UserAnimeStatus.Watching && !item.DateStarted.HasValue)
            {
                item.DateStarted = existing?.DateStarted ?? DateTime.Today;
            }
            else if (status == UserAnimeStatus.Completed)
            {
                item.DateStarted ??= existing?.DateStarted ?? DateTime.Today;
                item.DateCompleted ??= existing?.DateCompleted ?? DateTime.Today;
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
        string actionType = status switch
        {
            UserAnimeStatus.Completed => "Completed",
            UserAnimeStatus.Dropped => "Dropped",
            UserAnimeStatus.Watching => displayProgress == 0 ? "AddedToList" : (isManga ? "Read" : "Watched"),
            UserAnimeStatus.PlanToWatch => "PlanToWatch",
            UserAnimeStatus.OnHold => "OnHold",
            _ => "AddedToList"
        };

        try
        {
            await _historyService.AddEntryAsync(
                item.Id,
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
        WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());

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
            return await RemoveFromListAsync(originalItem.Id, ct);
        }

        bool markedAsDropped = originalItem.Status != UserAnimeStatus.Dropped && updatedItem.Status == UserAnimeStatus.Dropped;
        bool markedAsCompleted = originalItem.Status != UserAnimeStatus.Completed && updatedItem.Status == UserAnimeStatus.Completed;
        bool statusChanged = originalItem.Status != updatedItem.Status;

        string rawScore = updatedItem.Score;
        if (rawScore != "-" && rawScore.Contains(' '))
        {
            updatedItem.Score = rawScore[..rawScore.IndexOf(' ')];
        }

        bool scoreChanged = originalItem.Score != updatedItem.Score && updatedItem.Score != "-" && !string.IsNullOrEmpty(updatedItem.Score);

        await _uiDispatcher.InvokeAsync(() =>
        {
            if (markedAsCompleted)
            {
                updatedItem.DateCompleted ??= DateTime.Today;
                updatedItem.DateStarted ??= DateTime.Today;
            }
            else if (updatedItem.Status == UserAnimeStatus.Watching && !updatedItem.DateStarted.HasValue)
            {
                updatedItem.DateStarted = DateTime.Today;
            }

            updatedItem.CopyTo(originalItem);
        });

        await _animeRepository.AddOrUpdateAnimeAsync(originalItem);

        int displayProgress = originalItem.MediaKind == MediaKind.Anime ? originalItem.Progress : originalItem.ChaptersRead;

        try
        {
            bool isManga = originalItem.MediaKind != MediaKind.Anime;
            string statusAction = originalItem.Status switch
            {
                UserAnimeStatus.Dropped => "Dropped",
                UserAnimeStatus.Completed => "Completed",
                UserAnimeStatus.Watching => displayProgress == 0 ? "AddedToList" : (isManga ? "Read" : "Watched"),
                UserAnimeStatus.PlanToWatch => "PlanToWatch",
                UserAnimeStatus.OnHold => "OnHold",
                _ => "AddedToList"
            };

            if (markedAsDropped)
                await _historyService.AddEntryAsync(originalItem.Id, originalItem.Title, originalItem.RussianTitle, displayProgress, "Dropped", null, originalItem.MainPictureUrl, ct);
            else if (markedAsCompleted)
                await _historyService.AddEntryAsync(originalItem.Id, originalItem.Title, originalItem.RussianTitle, displayProgress, "Completed", null, originalItem.MainPictureUrl, ct);
            else if (statusChanged)
                await _historyService.AddEntryAsync(originalItem.Id, originalItem.Title, originalItem.RussianTitle, displayProgress, statusAction, null, originalItem.MainPictureUrl, ct);

            if (scoreChanged)
                await _historyService.AddEntryAsync(originalItem.Id, originalItem.Title, originalItem.RussianTitle, displayProgress, "ScoreSet", originalItem.Score, originalItem.MainPictureUrl, ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AnimeListActionService: Failed to record history during SaveAnimeAsync for {Id}", originalItem.Id);
        }

        WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());

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
        if (_airingInfoService == null || item.MediaKind != MediaKind.Anime) return;
        if (item.Status != UserAnimeStatus.Watching) return;

        var statusDetailed = item.StatusDetailed?.ToLowerInvariant();
        bool isOngoing = statusDetailed is "currently_airing" or "currently airing" or "ongoing" or "releasing"
            || AppConstants.AiringStatus.IsCurrentlyAiring(statusDetailed)
            || (!AppConstants.AiringStatus.IsFinishedAiring(statusDetailed) && (item.TotalEpisodes == 0 || item.EpisodesAired < item.TotalEpisodes))
            || item.NextEpisodeAt.HasValue;

        if (isOngoing)
        {
            Task.Run(async () =>
            {
                try
                {
                    await _airingInfoService.SyncEpisodesForAnimeAsync(item);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "AnimeListActionService: Immediate airing sync failed for {Title} ({Id})", item.Title, item.Id);
                }
            });
        }
    }

    public async Task<ListActionResult> RemoveFromListAsync(
        int animeId,
        CancellationToken ct = default)
    {
        if (animeId <= 0) return ListActionResult.Fail("Invalid anime ID");

        await _progressService.RemoveAnimeAsync(animeId);
        WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());

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
