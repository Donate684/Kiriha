using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public sealed class LibraryMutationService : ILibraryMutationService
{
    private readonly IAnimeRepository? _animeRepository;
    private readonly IUserAnimeRepository? _userAnimeRepo;
    private readonly ISyncManager? _syncManager;
    private readonly IUiDispatcher? _uiDispatcher;
    private readonly ICoreEventBus _eventBus;
    private readonly TimeProvider _clock;

    public LibraryMutationService(
        IAnimeRepository? animeRepository,
        IUserAnimeRepository? userAnimeRepo,
        ISyncManager? syncManager,
        IUiDispatcher? uiDispatcher,
        ICoreEventBus? eventBus = null,
        TimeProvider? clock = null)
    {
        _animeRepository = animeRepository;
        _userAnimeRepo = userAnimeRepo;
        _syncManager = syncManager;
        _uiDispatcher = uiDispatcher;
        _eventBus = eventBus ?? CoreEventBus.Default;
        _clock = clock ?? TimeProvider.System;
    }

    public DateTime Today => _clock.GetLocalNow().Date;

    public async Task<AnimeEntity?> FindExistingInMemoryAsync(int animeId, MediaKind kind)
    {
        if (_animeRepository == null)
            return null;

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

    public async Task<AnimeEntity?> FindExistingAsync(int animeId, MediaKind kind, CancellationToken ct = default)
    {
        var inMemory = await FindExistingInMemoryAsync(animeId, kind);
        if (inMemory != null || _userAnimeRepo == null)
            return inMemory;

        var fromDb = await _userAnimeRepo.GetByIdAsync(animeId, kind, ct);
        if (fromDb != null)
            return fromDb;

        return kind == MediaKind.Anime ? await _userAnimeRepo.GetByIdAsync(animeId, MediaKind.Anime, ct) : null;
    }

    public UserAnimeStatus? ComputeSmartNextStatus(AnimeEntity item, int nextProgress)
    {
        bool isManga = item.MediaKind != MediaKind.Anime;
        int totalUnits = isManga ? item.Chapters : item.TotalEpisodes;

        UserAnimeStatus? nextStatus = null;
        if (item.Status != UserAnimeStatus.Watching && item.Status != UserAnimeStatus.Completed)
        {
            nextStatus = UserAnimeStatus.Watching;
        }

        if (totalUnits > 0 &&
            nextProgress >= totalUnits &&
            (item.Status == UserAnimeStatus.Watching || nextStatus == UserAnimeStatus.Watching || item.IsRewatching))
        {
            nextStatus = UserAnimeStatus.Completed;
        }

        return nextStatus;
    }

    public void StampStatusDates(AnimeEntity item, UserAnimeStatus? nextStatus, bool isStarting, AnimeEntity? existing = null)
    {
        var today = Today;
        if (nextStatus == UserAnimeStatus.Completed)
        {
            item.DateCompleted ??= existing?.DateCompleted ?? today;
            item.DateStarted ??= existing?.DateStarted ?? today;
            item.IsRewatching = false;
        }
        else if (isStarting && !item.DateStarted.HasValue)
        {
            item.DateStarted = existing?.DateStarted ?? today;
        }
    }

    public async Task<bool> ApplyLocalProgressAsync(
        AnimeEntity item,
        int nextProgress,
        UserAnimeStatus? nextStatus = null,
        CancellationToken ct = default)
    {
        if ((nextStatus == UserAnimeStatus.Watching || nextStatus == UserAnimeStatus.Completed) &&
            AppConstants.AiringStatus.IsNotYetAired(item.StatusDetailed))
        {
            Log.Warning("Cannot set {Title} to {Status} - it has not aired yet.", item.Title, nextStatus);
            return false;
        }

        bool isManga = item.MediaKind != MediaKind.Anime;
        int currentProgress = isManga ? item.ChaptersRead : item.Progress;
        bool isStarting = nextStatus == UserAnimeStatus.Watching ||
                          (nextProgress > 0 && currentProgress == 0 && item.Status == UserAnimeStatus.None);

        void MutateOnUi()
        {
            StampStatusDates(item, nextStatus, isStarting);

            if (isManga)
                item.ChaptersRead = nextProgress;
            else
                item.Progress = nextProgress;

            if (nextStatus.HasValue && nextStatus != UserAnimeStatus.None)
                item.Status = nextStatus.Value;

            if (_animeRepository != null &&
                item.Status != UserAnimeStatus.None &&
                _animeRepository.FindById(item.Id, item.MediaKind) == null &&
                (_animeRepository.Collection == null || !_animeRepository.Collection.Any(x => x.Id == item.Id && x.MediaKind == item.MediaKind)))
            {
                _animeRepository.AddToCollection(item);
            }

            if (!isManga)
            {
                _eventBus.Publish(new AnimeProgressChangedMessage(item, nextProgress));
            }
        }

        if (_uiDispatcher != null)
            await _uiDispatcher.InvokeAsync(MutateOnUi);
        else
            MutateOnUi();

        if (_userAnimeRepo != null)
        {
            await _userAnimeRepo.UpdateProgressAsync(item, nextProgress, nextStatus, ct);
        }

        return true;
    }

    public async Task EnqueueProgressSyncAsync(AnimeEntity item, int nextProgress, UserAnimeStatus? nextStatus = null)
    {
        if (_syncManager == null)
            return;

        bool isManga = item.MediaKind != MediaKind.Anime;
        if (isManga || nextStatus.HasValue)
            await _syncManager.EnqueueFullUpdateAsync(item);
        else
            await _syncManager.EnqueueUpdateAsync(
                item.Id,
                item.MediaKind,
                nextProgress,
                nextStatus ?? (item.Status != UserAnimeStatus.None ? item.Status : null));
    }
}
