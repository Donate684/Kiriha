using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public partial class AnimeProgressService : IProgressUpdateService, IDisposable
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

    public Task<ScrobbleResult> RecordScrobbleAsync(MediaKey key, long generation, int episode, CancellationToken ct) =>
        SerializeProgressChangeAsync(async () =>
        {
            if (await FindCurrentEntryAsync(new LibraryEntryTarget(key, generation), ct) is not { } entry)
                return new ScrobbleResult(ScrobbleOutcome.EntryChanged);
            var (item, persisted) = entry;
            if (item.Progress >= episode || persisted.Progress >= episode ||
                (item.Status == UserAnimeStatus.Completed && !item.IsRewatching))
                return new ScrobbleResult(ScrobbleOutcome.AlreadyWatched);

            var nextStatus = _mutations.ComputeSmartNextStatus(item, episode);
            bool saved = await SaveProgressChangeAsync(item, episode, nextStatus, HistoryActionType.Scrobbled, onlyAdvance: true, ct);
            return saved ? new ScrobbleResult(ScrobbleOutcome.Saved, item, nextStatus) : default;
        }, ct);

    public virtual Task<UserAnimeStatus?> SmartIncrementProgressAsync(AnimeEntity item, int nextProgress)
    {
        if (_entryWriter != null)
            return _animeRepository.CaptureEntry(item) is { } target
                ? SetObservedProgressAsync(target, ReadProgress(item), nextProgress)
                : Task.FromResult<UserAnimeStatus?>(null);

        return SerializeProgressChangeAsync(
            async () => (await SaveWatchedAsync(item, nextProgress, onlyAdvance: false)).Status, CancellationToken.None);
    }

    public Task<bool> MarkEpisodeWatchedAsync(MediaKey key, long generation, int episode) =>
        SerializeProgressChangeAsync(async () =>
        {
            if (key.Kind != MediaKind.Anime || episode <= 0 ||
                await FindCurrentEntryAsync(new LibraryEntryTarget(key, generation), CancellationToken.None) is not { } entry ||
                entry.Item.Status == UserAnimeStatus.Completed)
                return false;

            return (await SaveWatchedAsync(entry.Item, episode, onlyAdvance: true)).Saved;
        }, CancellationToken.None);

    private async Task<(bool Saved, UserAnimeStatus? Status)> SaveWatchedAsync(AnimeEntity item, int progress, bool onlyAdvance)
    {
        var nextStatus = _mutations.ComputeSmartNextStatus(item, progress);
        if (!await SaveProgressChangeAsync(
                item, progress, nextStatus, HistoryActionType.Watched, onlyAdvance, CancellationToken.None))
            return (false, null);

        if (nextStatus == UserAnimeStatus.Completed)
            _eventBus.Publish(new AnimeCompletedRatingPromptMessage(item) { Target = _animeRepository.CaptureEntry(item) });
        if (nextStatus.HasValue)
            _eventBus.Publish(new AnimeListRefreshMessage());

        return (true, nextStatus);
    }

    public Task SmartDecrementProgressAsync(AnimeEntity item)
    {
        if (_entryWriter != null)
            return _animeRepository.CaptureEntry(item) is { } target ? ChangeProgressAsync(target, -1) : Task.CompletedTask;

        return SerializeProgressChangeAsync(async () =>
        {
            int currentProgress = ReadProgress(item);
            return currentProgress > 0 && await SaveProgressChangeAsync(
                item, currentProgress - 1, null, HistoryActionType.Reverted, onlyAdvance: false, CancellationToken.None);
        }, CancellationToken.None);
    }

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
        if (_entryWriter != null)
        {
            if (_animeRepository.CaptureEntry(item) is { } target)
                await SaveScoreAsync(target, null, score);
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
        if (_entryWriter != null)
        {
            if (_animeRepository.CaptureEntry(item) is { } target)
                await ConfirmRewatchAsync(target, item.RewatchCount, episode);
            return;
        }

        await _uiDispatcher.InvokeAsync(() =>
        {
            ApplyRewatch(item, episode);
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

    public void Dispose()
    {
        if (_ownsSyncGate)
            _syncGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
