using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
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

    public AnimeProgressService(
        IAnimeRepository animeRepository,
        IUserAnimeRepository userAnimeRepo,
        ISyncManager syncManager,
        IHistoryService historyService,
        IUiDispatcher uiDispatcher)
    {
        _animeRepository = animeRepository;
        _userAnimeRepo = userAnimeRepo;
        _syncManager = syncManager;
        _historyService = historyService;
        _uiDispatcher = uiDispatcher;
    }

    public async Task RemoveAnimeAsync(int animeId)
    {
        var item = _animeRepository.Collection.FirstOrDefault(x => x.Id == animeId)
                ?? await _userAnimeRepo.GetByIdAsync(animeId);

        if (item != null)
        {
            await _historyService.AddEntryAsync(
                item.Id,
                item.Title,
                item.RussianTitle,
                item.Progress,
                "Deleted",
                detail: null,
                posterUrl: item.MainPictureUrl);
        }

        // Remove locally first so the UI is responsive even when offline.
        await _animeRepository.RemoveAnimeLocalAsync(animeId);

        // Persist a Remove sync task so the deletion is replayed against trackers
        try
        {
            await _syncManager.EnqueueRemoveAsync(animeId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AnimeProgressService: Failed to enqueue Remove sync task for {AnimeId}", animeId);
        }
    }

    /// <summary>
    /// Public full-flow overload used by external callers (ScrobbleService, NowPlayingViewModel, etc.).
    /// Applies local DB changes and enqueues sync. Does NOT record a history entry — callers own that.
    /// </summary>
    public virtual async Task<bool> UpdateProgressAsync(AnimeEntity item, int nextProgress, UserAnimeStatus? nextStatus = null)
    {
        if (!await ApplyLocalProgressAsync(item, nextProgress, nextStatus)) return false;

        if (nextStatus.HasValue)
            await _syncManager.EnqueueFullUpdateAsync(item);
        else
            await _syncManager.EnqueueUpdateAsync(item.Id, nextProgress, nextStatus);

        return true;
    }

    public virtual async Task<UserAnimeStatus?> SmartIncrementProgressAsync(AnimeEntity item, int nextProgress)
    {
        UserAnimeStatus? nextStatus = null;
        if (item.Status != UserAnimeStatus.Watching && item.Status != UserAnimeStatus.Completed)
            nextStatus = UserAnimeStatus.Watching;

        bool isManga = item.MediaKind != MediaKind.Anime;

        // Manga completion
        if (isManga && item.Chapters > 0 && nextProgress >= item.Chapters && item.Status == UserAnimeStatus.Watching)
            nextStatus = UserAnimeStatus.Completed;
        // Anime completion
        else if (!isManga && item.TotalEpisodes > 0 && nextProgress >= item.TotalEpisodes && item.Status == UserAnimeStatus.Watching)
            nextStatus = UserAnimeStatus.Completed;

        if (isManga)
        {
            bool isStarting = nextStatus == UserAnimeStatus.Watching;
            bool isCompleting = nextStatus == UserAnimeStatus.Completed;

            await _uiDispatcher.InvokeAsync(() =>
            {
                if (isStarting && !item.DateStarted.HasValue)
                {
                    item.DateStarted = DateTime.Today;
                }
                if (isCompleting)
                {
                    item.DateCompleted ??= DateTime.Today;
                    item.DateStarted ??= DateTime.Today;
                }
                item.ChaptersRead = nextProgress;
                if (nextStatus.HasValue && nextStatus != UserAnimeStatus.None)
                    item.Status = nextStatus.Value;
            });

            await _userAnimeRepo.UpdateProgressAsync(item, nextProgress, nextStatus);

            if (nextStatus == UserAnimeStatus.Completed)
            {
                WeakReferenceMessenger.Default.Send(new AnimeCompletedRatingPromptMessage(item));
            }

            // Write history BEFORE sync so the DB row exists when SetPendingTrackers fires.
            await _historyService.AddEntryAsync(item.Id, item.Title, item.RussianTitle, nextProgress,
                nextStatus == UserAnimeStatus.Completed ? "Completed" : "Read", null, item.MainPictureUrl);
            await _syncManager.EnqueueFullUpdateAsync(item);
            return nextStatus;
        }
        else
        {
            if (await ApplyLocalProgressAsync(item, nextProgress, nextStatus))
            {
                if (nextStatus == UserAnimeStatus.Completed)
                {
                    WeakReferenceMessenger.Default.Send(new AnimeCompletedRatingPromptMessage(item));
                }

                // Write history BEFORE sync so the DB row exists when SetPendingTrackers fires.
                await _historyService.AddEntryAsync(item.Id, item.Title, item.RussianTitle, nextProgress,
                    nextStatus == UserAnimeStatus.Completed ? "Completed" : "Watched", null, item.MainPictureUrl);

                if (nextStatus.HasValue)
                    await _syncManager.EnqueueFullUpdateAsync(item);
                else
                    await _syncManager.EnqueueUpdateAsync(item.Id, nextProgress);

                return nextStatus;
            }
        }

        return null;
    }

    public async Task SmartDecrementProgressAsync(AnimeEntity item)
    {
        bool isManga = item.MediaKind != MediaKind.Anime;

        if (isManga)
        {
            if (item.ChaptersRead > 0)
            {
                int nextProgress = item.ChaptersRead - 1;
                await _uiDispatcher.InvokeAsync(() =>
                {
                    item.ChaptersRead = nextProgress;
                });

                await _userAnimeRepo.UpdateProgressAsync(item, nextProgress, null);
                // Write history BEFORE sync.
                await _historyService.AddEntryAsync(item.Id, item.Title, item.RussianTitle, nextProgress, "Reverted", null, item.MainPictureUrl);
                await _syncManager.EnqueueFullUpdateAsync(item);
            }
        }
        else
        {
            if (item.Progress > 0)
            {
                int nextProgress = item.Progress - 1;
                if (await ApplyLocalProgressAsync(item, nextProgress))
                {
                    // Write history BEFORE sync.
                    await _historyService.AddEntryAsync(item.Id, item.Title, item.RussianTitle, nextProgress, "Reverted", null, item.MainPictureUrl);
                    await _syncManager.EnqueueUpdateAsync(item.Id, nextProgress);
                }
            }
        }
    }

    public async Task SetScoreAsync(AnimeEntity item, int score)
    {
        await _uiDispatcher.InvokeAsync(() =>
        {
            item.Score = score.ToString();
        });
        await _userAnimeRepo.UpdateScoreAsync(item, item.Score);
        // Write history BEFORE sync.
        await _historyService.AddEntryAsync(item.Id, item.Title, item.RussianTitle, item.Progress, "ScoreSet", score.ToString(), item.MainPictureUrl);
        await _syncManager.EnqueueUpdateAsync(item.Id, item.Progress, score: score);
    }

    public async Task ConfirmRewatchAsync(AnimeEntity item, int episode = 1)
    {
        await _uiDispatcher.InvokeAsync(() =>
        {
            item.IsRewatching = true;
            item.RewatchCount++;
            item.Progress = episode;
            item.Status = UserAnimeStatus.Watching;
            item.DateStarted = DateTime.Today;
            item.DateCompleted = null;
        });

        await _userAnimeRepo.UpdateProgressAsync(item, episode, UserAnimeStatus.Watching);
        // Write history BEFORE sync.
        await _historyService.AddEntryAsync(item.Id, item.Title, item.RussianTitle, episode, "Rewatching", null, item.MainPictureUrl);
        await _syncManager.EnqueueFullUpdateAsync(item);
    }

    /// <summary>
    /// Applies progress changes to the local DB and UI state only — does NOT enqueue any sync task.
    /// Used internally so history can be written (via AddEntryAsync) before the sync enqueue fires
    /// SetPendingTrackers, ensuring the DB row already exists when SetPendingTrackers tries to update it.
    /// </summary>
    private async Task<bool> ApplyLocalProgressAsync(AnimeEntity item, int nextProgress, UserAnimeStatus? nextStatus = null)
    {
        if ((nextStatus == UserAnimeStatus.Watching || nextStatus == UserAnimeStatus.Completed) && item.StatusDetailed == "Not yet aired")
        {
            Log.Warning("Cannot set {Title} to {Status} - it has not aired yet.", item.Title, nextStatus);
            return false;
        }

        bool isStarting = nextStatus == UserAnimeStatus.Watching || (nextProgress > 0 && item.Progress == 0 && item.Status == UserAnimeStatus.None);
        bool isCompleting = nextStatus == UserAnimeStatus.Completed;

        await _uiDispatcher.InvokeAsync(() =>
        {
            if (isStarting && !item.DateStarted.HasValue)
                item.DateStarted = DateTime.Today;
            if (isCompleting)
            {
                item.DateCompleted ??= DateTime.Today;
                item.DateStarted ??= DateTime.Today;
            }
            item.Progress = nextProgress;
            if (nextStatus.HasValue && nextStatus != UserAnimeStatus.None)
                item.Status = nextStatus.Value;
        });

        await _userAnimeRepo.UpdateProgressAsync(item, nextProgress, nextStatus);
        return true;
    }
}
