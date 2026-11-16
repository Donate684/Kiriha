using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
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

    private async Task<AnimeEntity?> FindExistingInMemoryAsync(int animeId, MediaKind kind)
    {
        if (_animeRepository == null) return null;

        var indexed = _animeRepository.FindById(animeId, kind);
        if (indexed != null || _animeRepository.TryGetSnapshot() != null)
            return indexed;

        if (_uiDispatcher == null || !_uiDispatcher.HasThreadAffinity || _uiDispatcher.CheckAccess())
            return _animeRepository.Collection?.FirstOrDefault(x => x.Id == animeId && x.MediaKind == kind);

        AnimeEntity? matched = null;
        await _uiDispatcher.InvokeAsync(() =>
        {
            matched = _animeRepository.Collection?.FirstOrDefault(x => x.Id == animeId && x.MediaKind == kind);
        });
        return matched;
    }

    public Task RemoveAnimeAsync(int animeId) => RemoveAnimeAsync(animeId, MediaKind.Anime);

    public async Task RemoveAnimeAsync(int animeId, MediaKind mediaKind)
    {
        bool isManga = mediaKind != MediaKind.Anime;
        var item = await FindExistingInMemoryAsync(animeId, mediaKind)
                ?? await _userAnimeRepo.GetByIdAsync(animeId, mediaKind)
                ?? (!isManga ? await _userAnimeRepo.GetByIdAsync(animeId) : null);

        if (item != null)
        {
            int historyProgress = isManga ? item.ChaptersRead : item.Progress;
            using (HistoryContext.UseMediaKind(item.MediaKind))
            {
                await _historyService.AddEntryAsync(
                    item.Id,
                    item.Title,
                    item.RussianTitle,
                    historyProgress,
                    "Deleted",
                    detail: null,
                    posterUrl: item.MainPictureUrl);
            }
        }

        // Remove locally first so the UI is responsive even when offline.
        if (isManga)
            await _animeRepository.RemoveAnimeLocalAsync(animeId, mediaKind);
        else
            await _animeRepository.RemoveAnimeLocalAsync(animeId);

        // Persist a Remove sync task so the deletion is replayed against trackers
        try
        {
            if (isManga)
                await _syncManager.EnqueueRemoveAsync(animeId, mediaKind);
            else
                await _syncManager.EnqueueRemoveAsync(animeId);
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
    public virtual async Task<bool> UpdateProgressAsync(AnimeEntity item, int nextProgress, UserAnimeStatus? nextStatus = null)
    {
        if (!await ApplyLocalProgressAsync(item, nextProgress, nextStatus)) return false;

        if (nextStatus.HasValue)
            await _syncManager.EnqueueFullUpdateAsync(item);
        else if (item.MediaKind == MediaKind.Anime)
            await _syncManager.EnqueueUpdateAsync(item.Id, nextProgress, nextStatus);
        else
            await _syncManager.EnqueueUpdateAsync(item.Id, item.MediaKind, nextProgress, nextStatus);

        return true;
    }

    public virtual async Task<UserAnimeStatus?> SmartIncrementProgressAsync(AnimeEntity item, int nextProgress)
    {
        UserAnimeStatus? nextStatus = null;
        if (item.Status != UserAnimeStatus.Watching && item.Status != UserAnimeStatus.Completed)
            nextStatus = UserAnimeStatus.Watching;

        bool isManga = item.MediaKind != MediaKind.Anime;

        if (isManga && item.Chapters > 0 && nextProgress >= item.Chapters && (item.Status == UserAnimeStatus.Watching || nextStatus == UserAnimeStatus.Watching))
            nextStatus = UserAnimeStatus.Completed;
        else if (!isManga && item.TotalEpisodes > 0 && nextProgress >= item.TotalEpisodes && (item.Status == UserAnimeStatus.Watching || nextStatus == UserAnimeStatus.Watching))
            nextStatus = UserAnimeStatus.Completed;

        if (!await ApplyLocalProgressAsync(item, nextProgress, nextStatus))
            return null;

        if (nextStatus == UserAnimeStatus.Completed)
        {
            WeakReferenceMessenger.Default.Send(new AnimeCompletedRatingPromptMessage(item));
        }

        // Write history BEFORE sync so the DB row exists when SetPendingTrackers fires.
        using (HistoryContext.UseMediaKind(item.MediaKind))
        {
            var actionName = nextStatus == UserAnimeStatus.Completed
                ? "Completed"
                : (isManga ? "Read" : "Watched");
            await _historyService.AddEntryAsync(item.Id, item.Title, item.RussianTitle, nextProgress, actionName, null, item.MainPictureUrl);
        }

        if (isManga || nextStatus.HasValue)
            await _syncManager.EnqueueFullUpdateAsync(item);
        else
            await _syncManager.EnqueueUpdateAsync(item.Id, nextProgress);

        if (nextStatus.HasValue)
        {
            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
        }

        return nextStatus;
    }

    public async Task SmartDecrementProgressAsync(AnimeEntity item)
    {
        bool isManga = item.MediaKind != MediaKind.Anime;
        int currentProgress = isManga ? item.ChaptersRead : item.Progress;
        if (currentProgress <= 0) return;

        int nextProgress = currentProgress - 1;
        if (!await ApplyLocalProgressAsync(item, nextProgress))
            return;

        using (HistoryContext.UseMediaKind(item.MediaKind))
        {
            await _historyService.AddEntryAsync(item.Id, item.Title, item.RussianTitle, nextProgress, "Reverted", null, item.MainPictureUrl);
        }

        if (isManga)
            await _syncManager.EnqueueFullUpdateAsync(item);
        else
            await _syncManager.EnqueueUpdateAsync(item.Id, nextProgress);
    }

    public async Task SetScoreAsync(AnimeEntity item, int score)
    {
        await _uiDispatcher.InvokeAsync(() =>
        {
            item.Score = score <= 0 ? "-" : score.ToString();
        });
        await _userAnimeRepo.UpdateScoreAsync(item, item.Score);

        bool isManga = item.MediaKind != MediaKind.Anime;
        int displayProgress = isManga ? item.ChaptersRead : item.Progress;

        // Write history BEFORE sync.
        using (HistoryContext.UseMediaKind(item.MediaKind))
        {
            await _historyService.AddEntryAsync(item.Id, item.Title, item.RussianTitle, displayProgress, "ScoreSet", score.ToString(), item.MainPictureUrl);
        }

        if (isManga)
            await _syncManager.EnqueueFullUpdateAsync(item);
        else
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
            WeakReferenceMessenger.Default.Send(new AnimeProgressChangedMessage(item, episode));
        });

        await _userAnimeRepo.UpdateProgressAsync(item, episode, UserAnimeStatus.Watching);
        using (HistoryContext.UseMediaKind(item.MediaKind))
        {
            await _historyService.AddEntryAsync(item.Id, item.Title, item.RussianTitle, episode, "Rewatching", null, item.MainPictureUrl);
        }
        await _syncManager.EnqueueFullUpdateAsync(item);
    }

    /// <summary>
    /// Applies progress changes to the local DB and UI state only — does NOT enqueue any sync task.
    /// Used internally so history can be written (via AddEntryAsync) before the sync enqueue fires
    /// SetPendingTrackers, ensuring the DB row already exists when SetPendingTrackers tries to update it.
    /// </summary>
    private async Task<bool> ApplyLocalProgressAsync(AnimeEntity item, int nextProgress, UserAnimeStatus? nextStatus = null)
    {
        if ((nextStatus == UserAnimeStatus.Watching || nextStatus == UserAnimeStatus.Completed) &&
            AppConstants.AiringStatus.IsNotYetAired(item.StatusDetailed))
        {
            Log.Warning("Cannot set {Title} to {Status} - it has not aired yet.", item.Title, nextStatus);
            return false;
        }

        bool isManga = item.MediaKind != MediaKind.Anime;
        int currentProgress = isManga ? item.ChaptersRead : item.Progress;
        bool isStarting = nextStatus == UserAnimeStatus.Watching || (nextProgress > 0 && currentProgress == 0 && item.Status == UserAnimeStatus.None);
        bool isCompleting = nextStatus == UserAnimeStatus.Completed;

        await _uiDispatcher.InvokeAsync(() =>
        {
            if (isStarting && !item.DateStarted.HasValue)
                item.DateStarted = DateTime.Today;
            if (isCompleting)
            {
                item.DateCompleted ??= DateTime.Today;
                item.DateStarted ??= DateTime.Today;
                item.IsRewatching = false;
            }

            if (isManga)
                item.ChaptersRead = nextProgress;
            else
                item.Progress = nextProgress;

            if (nextStatus.HasValue && nextStatus != UserAnimeStatus.None)
                item.Status = nextStatus.Value;

            // Keep AnimeRepository in-memory index & collection synchronized when a detached item enters the library.
            if (_animeRepository != null &&
                item.Status != UserAnimeStatus.None &&
                _animeRepository.FindById(item.Id, item.MediaKind) == null &&
                (_animeRepository.Collection == null || !_animeRepository.Collection.Any(x => x.Id == item.Id && x.MediaKind == item.MediaKind)))
            {
                _animeRepository.AddToCollection(item);
            }

            if (!isManga)
            {
                WeakReferenceMessenger.Default.Send(new AnimeProgressChangedMessage(item, nextProgress));
            }
        });

        await _userAnimeRepo.UpdateProgressAsync(item, nextProgress, nextStatus);
        return true;
    }
}
