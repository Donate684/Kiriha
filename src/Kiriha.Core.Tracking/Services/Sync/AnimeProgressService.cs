using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public class AnimeProgressService : IProgressUpdateService
{
    private readonly IAnimeRepository _animeRepository;
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly ISyncManager _syncManager;
    private readonly IHistoryService _historyService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILibraryMutationService _mutations;
    private readonly ICoreEventBus _eventBus;
    private readonly TimeProvider _clock;

    public AnimeProgressService(
        IAnimeRepository animeRepository,
        IUserAnimeRepository userAnimeRepo,
        ISyncManager syncManager,
        IHistoryService historyService,
        IUiDispatcher uiDispatcher,
        ILibraryMutationService? mutations = null,
        ICoreEventBus? eventBus = null,
        TimeProvider? clock = null)
    {
        _animeRepository = animeRepository;
        _userAnimeRepo = userAnimeRepo;
        _syncManager = syncManager;
        _historyService = historyService;
        _uiDispatcher = uiDispatcher;
        _eventBus = eventBus ?? CoreEventBus.Default;
        _clock = clock ?? TimeProvider.System;
        _mutations = mutations ?? new LibraryMutationService(
            animeRepository,
            userAnimeRepo,
            syncManager,
            uiDispatcher,
            _eventBus,
            _clock);
    }

    public Task RemoveAnimeAsync(int animeId) => RemoveAnimeAsync(animeId, MediaKind.Anime);

    public async Task RemoveAnimeAsync(int animeId, MediaKind mediaKind)
    {
        bool isManga = mediaKind != MediaKind.Anime;
        var item = await _mutations.FindExistingAsync(animeId, mediaKind);

        if (item != null)
        {
            int historyProgress = isManga ? item.ChaptersRead : item.Progress;
            await _historyService.AddEntryAsync(
                item.Id,
                item.MediaKind,
                item.Title,
                item.RussianTitle,
                historyProgress,
                HistoryActionType.Deleted,
                detail: null,
                posterUrl: item.MainPictureUrl);
        }

        // Remove locally first so the UI is responsive even when offline.
        if (isManga)
            await _animeRepository.RemoveAnimeLocalAsync(animeId, mediaKind);
        else
            await _animeRepository.RemoveAnimeLocalAsync(animeId);

        // Persist a Remove sync task so the deletion is replayed against trackers
        try
        {
            await _syncManager.EnqueueRemoveAsync(animeId, mediaKind);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AnimeProgressService: Failed to enqueue Remove sync task for {AnimeId} ({MediaKind})", animeId, mediaKind);
        }
    }

    /// <summary>
    /// Public full-flow overload used by external callers (ScrobbleService, NowPlayingViewModel, etc.).
    /// Applies local DB changes and enqueues sync. Does NOT record a history entry — callers own that.
    /// </summary>
    public virtual async Task<bool> UpdateProgressAsync(AnimeEntity anime, int nextProgress, UserAnimeStatus? nextStatus = null)
    {
        if (!await _mutations.ApplyLocalProgressAsync(anime, nextProgress, nextStatus))
            return false;

        await _mutations.EnqueueProgressSyncAsync(anime, nextProgress, nextStatus);
        return true;
    }

    public virtual async Task<UserAnimeStatus?> SmartIncrementProgressAsync(AnimeEntity item, int nextProgress)
    {
        var nextStatus = _mutations.ComputeSmartNextStatus(item, nextProgress);

        if (!await _mutations.ApplyLocalProgressAsync(item, nextProgress, nextStatus))
            return null;

        if (nextStatus == UserAnimeStatus.Completed)
        {
            _eventBus.Publish(new AnimeCompletedRatingPromptMessage(item));
        }

        // Write history BEFORE sync so the DB row exists when SetPendingTrackers fires.
        var actionType = nextStatus == UserAnimeStatus.Completed
            ? HistoryActionType.Completed
            : HistoryActionType.Watched;
        await _historyService.AddEntryAsync(
            item.Id,
            item.MediaKind,
            item.Title,
            item.RussianTitle,
            nextProgress,
            actionType,
            null,
            item.MainPictureUrl);

        await _mutations.EnqueueProgressSyncAsync(item, nextProgress, nextStatus);

        if (nextStatus.HasValue)
        {
            _eventBus.Publish(new AnimeListRefreshMessage());
        }

        return nextStatus;
    }

    public async Task SmartDecrementProgressAsync(AnimeEntity item)
    {
        bool isManga = item.MediaKind != MediaKind.Anime;
        int currentProgress = isManga ? item.ChaptersRead : item.Progress;
        if (currentProgress <= 0)
            return;

        int nextProgress = currentProgress - 1;
        if (!await _mutations.ApplyLocalProgressAsync(item, nextProgress))
            return;

        await _historyService.AddEntryAsync(
            item.Id,
            item.MediaKind,
            item.Title,
            item.RussianTitle,
            nextProgress,
            HistoryActionType.Reverted,
            null,
            item.MainPictureUrl);

        await _mutations.EnqueueProgressSyncAsync(item, nextProgress);
    }

    public async Task SetScoreAsync(AnimeEntity item, int score)
    {
        await _uiDispatcher.InvokeAsync(() =>
        {
            item.UserScore = score > 0 ? score : null;
        });
        await _userAnimeRepo.UpdateScoreAsync(item, item.Score);

        bool isManga = item.MediaKind != MediaKind.Anime;
        int displayProgress = isManga ? item.ChaptersRead : item.Progress;

        // Write history BEFORE sync.
        await _historyService.AddEntryAsync(
            item.Id,
            item.MediaKind,
            item.Title,
            item.RussianTitle,
            displayProgress,
            HistoryActionType.ScoreSet,
            score.ToString(),
            item.MainPictureUrl);

        if (isManga)
            await _syncManager.EnqueueFullUpdateAsync(item);
        else
            await _syncManager.EnqueueUpdateAsync(
                item.Id,
                item.MediaKind,
                item.Progress,
                item.Status != UserAnimeStatus.None ? item.Status : null,
                score: score);
    }

    public async Task ConfirmRewatchAsync(AnimeEntity item, int episode = 1)
    {
        var today = _clock.GetLocalNow().Date;
        await _uiDispatcher.InvokeAsync(() =>
        {
            item.IsRewatching = true;
            item.RewatchCount++;
            item.Progress = episode;
            item.Status = UserAnimeStatus.Watching;
            item.DateStarted = today;
            item.DateCompleted = null;
            _eventBus.Publish(new AnimeProgressChangedMessage(item, episode));
        });

        await _userAnimeRepo.UpdateProgressAsync(item, episode, UserAnimeStatus.Watching);
        await _historyService.AddEntryAsync(
            item.Id,
            item.MediaKind,
            item.Title,
            item.RussianTitle,
            episode,
            HistoryActionType.Rewatching,
            null,
            item.MainPictureUrl);
        await _syncManager.EnqueueFullUpdateAsync(item);
    }
}
