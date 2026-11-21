using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public class AnimeProgressService : IProgressUpdateService, IDisposable
{
    private readonly IAnimeRepository _animeRepository;
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly ISyncManager _syncManager;
    private readonly IHistoryService _historyService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILibraryMutationService _mutations;
    private readonly ICoreEventBus _eventBus;
    private readonly TimeProvider _clock;
    private readonly LibrarySyncGate _syncGate;
    private readonly bool _ownsSyncGate;
    private readonly LibraryEntryWriter? _entryWriter;

    public AnimeProgressService(
        IAnimeRepository animeRepository,
        IUserAnimeRepository userAnimeRepo,
        ISyncManager syncManager,
        IHistoryService historyService,
        IUiDispatcher uiDispatcher,
        ILibraryMutationService? mutations = null,
        ICoreEventBus? eventBus = null,
        TimeProvider? clock = null,
        LibrarySyncGate? syncGate = null,
        LibraryEntryWriter? entryWriter = null)
    {
        _animeRepository = animeRepository;
        _userAnimeRepo = userAnimeRepo;
        _syncManager = syncManager;
        _historyService = historyService;
        _uiDispatcher = uiDispatcher;
        _eventBus = eventBus ?? CoreEventBus.Default;
        _clock = clock ?? TimeProvider.System;
        _syncGate = syncGate ?? new LibrarySyncGate();
        _ownsSyncGate = syncGate == null;
        _entryWriter = entryWriter;
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
        if (_entryWriter is { } writer)
        {
            using var write = await writer.AcquireWriteAsync(CancellationToken.None);
            var existing = await _mutations.FindExistingAsync(animeId, mediaKind);
            await writer.RemoveAsync(animeId, mediaKind, existing, CancellationToken.None);
            return;
        }

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
    /// Applies local DB changes and enqueues sync. Callers own the history entry.
    /// Automatic playback tracking uses <see cref="RecordScrobbleAsync"/>.
    /// </summary>
    public virtual async Task<bool> UpdateProgressAsync(AnimeEntity anime, int nextProgress, UserAnimeStatus? nextStatus = null)
    {
        if (!await _mutations.ApplyLocalProgressAsync(anime, nextProgress, nextStatus))
            return false;

        await _mutations.EnqueueProgressSyncAsync(anime, nextProgress, nextStatus);
        return true;
    }

    public Task<bool> RecordScrobbleAsync(AnimeEntity anime, int episode, UserAnimeStatus? nextStatus, CancellationToken ct) =>
        SerializeProgressChangeAsync(() => SaveProgressChangeAsync(
            anime, episode, nextStatus, HistoryActionType.Scrobbled, onlyAdvance: true, ct), ct);

    public virtual Task<UserAnimeStatus?> SmartIncrementProgressAsync(AnimeEntity item, int nextProgress) =>
        SerializeProgressChangeAsync(async () =>
        {
            var nextStatus = _mutations.ComputeSmartNextStatus(item, nextProgress);
            if (!await SaveProgressChangeAsync(
                    item, nextProgress, nextStatus, HistoryActionType.Watched, onlyAdvance: false, CancellationToken.None))
                return null;

            if (nextStatus == UserAnimeStatus.Completed)
                _eventBus.Publish(new AnimeCompletedRatingPromptMessage(item));
            if (nextStatus.HasValue)
                _eventBus.Publish(new AnimeListRefreshMessage());

            return nextStatus;
        }, CancellationToken.None);

    public Task SmartDecrementProgressAsync(AnimeEntity item) =>
        SerializeProgressChangeAsync(async () =>
        {
            int currentProgress = item.MediaKind == MediaKind.Anime ? item.Progress : item.ChaptersRead;
            return currentProgress > 0 && await SaveProgressChangeAsync(
                item, currentProgress - 1, null, HistoryActionType.Reverted, onlyAdvance: false, CancellationToken.None);
        }, CancellationToken.None);

    private async Task<bool> SaveProgressChangeAsync(
        AnimeEntity item, int progress, UserAnimeStatus? nextStatus, HistoryActionType action, bool onlyAdvance, CancellationToken ct)
    {
        var saved = await _mutations.ApplyProgressChangeAsync(item, progress, nextStatus, onlyAdvance, snapshot =>
            _syncManager.CommitProgressChangeAsync(snapshot, nextStatus, new HistoryItem
            {
                AnimeId = snapshot.Id,
                MediaKind = snapshot.MediaKind,
                AnimeTitle = snapshot.Title,
                RussianTitle = snapshot.RussianTitle,
                Episode = progress,
                Timestamp = _clock.GetUtcNow().UtcDateTime,
                ActionType = (int)(nextStatus == UserAnimeStatus.Completed ? HistoryActionType.Completed : action),
                PosterUrl = snapshot.MainPictureUrl
            }, onlyAdvance, ct));
        if (saved)
            _syncGate.RecordProgressChange(item.Id, item.MediaKind);
        return saved;
    }

    // Keep UI publication in the same order as commits, including when +/- overlaps with playback tracking.
    private async Task<T> SerializeProgressChangeAsync<T>(Func<Task<T>> saveAsync, CancellationToken ct)
    {
        using var scope = await _syncGate.AcquireProgressWriteAsync(ct);
        return await saveAsync();
    }

    public async Task SetScoreAsync(AnimeEntity item, int score)
    {
        if (_entryWriter is { } writer)
        {
            using var write = await writer.AcquireWriteAsync(CancellationToken.None);
            var draft = (await _mutations.FindExistingAsync(item.Id, item.MediaKind) ?? item).Clone();
            draft.UserScore = score > 0 ? score : null;
            await writer.SaveAsync(draft, [writer.CreateHistory(draft, HistoryActionType.ScoreSet, score.ToString())],
                CancellationToken.None, item);
            return;
        }

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
        if (_entryWriter is { } writer)
        {
            using var write = await writer.AcquireWriteAsync(CancellationToken.None);
            var draft = (await _mutations.FindExistingAsync(item.Id, item.MediaKind) ?? item).Clone();
            ApplyRewatch(draft);
            await writer.SaveAsync(draft, [writer.CreateHistory(draft, HistoryActionType.Rewatching)], CancellationToken.None, item);
            await _uiDispatcher.InvokeAsync(() => _eventBus.Publish(new AnimeProgressChangedMessage(item, episode)));
            return;
        }

        await _uiDispatcher.InvokeAsync(() =>
        {
            ApplyRewatch(item);
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

        void ApplyRewatch(AnimeEntity entry)
        {
            entry.IsRewatching = true;
            entry.RewatchCount++;
            entry.Progress = episode;
            entry.Status = UserAnimeStatus.Watching;
            entry.DateStarted = today;
            entry.DateCompleted = null;
        }
    }

    public void Dispose()
    {
        if (_ownsSyncGate)
            _syncGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
