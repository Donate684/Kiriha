using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public partial class AnimeListActionService : IAnimeListActionService
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
    private readonly LibraryEntryWriter? _entryWriter;

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
        TimeProvider? clock = null,
        LibraryEntryWriter? entryWriter = null)
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
        _entryWriter = entryWriter;
        _mutations = mutations ?? new LibraryMutationService(
            animeRepository,
            userAnimeRepo,
            syncManager,
            uiDispatcher,
            _eventBus,
            clock);
    }

    public Task<ListActionResult> AddToListAsync(
        AnimeEntity item,
        UserAnimeStatus status,
        int progress = 0,
        CancellationToken ct = default) =>
        AddToListAsync(item, status, progress, null, ct);

    public Task<ListActionResult> AddFromEditorAsync(
        AnimeEntity item, UserAnimeStatus status, long generation, CancellationToken ct = default) =>
        AddToListAsync(item, status, 0, generation, ct);

    private async Task<ListActionResult> AddToListAsync(
        AnimeEntity item, UserAnimeStatus status, int progress, long? generation, CancellationToken ct)
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

        using var write = _entryWriter != null ? await _entryWriter.AcquireWriteAsync(ct) : null;
        var existing = await _mutations.FindExistingAsync(item.Id, item.MediaKind, ct);
        if (generation is { } expected && (existing != null || !HasGeneration(item.Id, item.MediaKind, expected)))
            return EntryChanged();

        AnimeEntity draft = item.Clone();
        bool isManga = draft.MediaKind != MediaKind.Anime;

        await _uiDispatcher.InvokeAsync(() =>
        {
            if (existing != null)
            {
                draft.PreserveUserFieldsFrom(existing);
                if (draft.IsMissingDetails)
                    draft.MergeFullDetails(existing);
            }

            draft.Status = status;
            if (isManga)
            {
                if (progress > 0) draft.ChaptersRead = progress;
                else if (draft.ChaptersRead == 0 && existing != null && existing.ChaptersRead > 0) draft.ChaptersRead = existing.ChaptersRead;
            }
            else
            {
                if (progress > 0) draft.Progress = progress;
                else if (draft.Progress == 0 && existing != null && existing.Progress > 0) draft.Progress = existing.Progress;
            }

            _mutations.StampStatusDates(draft, status, isStarting: status == UserAnimeStatus.Watching, existing);
            if (status == UserAnimeStatus.Completed)
            {
                if (!isManga && draft.TotalEpisodes > 0 && draft.Progress < draft.TotalEpisodes)
                {
                    draft.Progress = draft.TotalEpisodes;
                }
                else if (isManga && draft.Chapters > 0 && draft.ChaptersRead < draft.Chapters)
                {
                    draft.ChaptersRead = draft.Chapters;
                }
            }
        });

        int displayProgress = isManga ? draft.ChaptersRead : draft.Progress;
        var actionType = status switch
        {
            UserAnimeStatus.Completed => HistoryActionType.Completed,
            UserAnimeStatus.Dropped => HistoryActionType.Dropped,
            UserAnimeStatus.Watching => displayProgress == 0 ? HistoryActionType.AddedToList : HistoryActionType.Watched,
            UserAnimeStatus.PlanToWatch => HistoryActionType.PlanToWatch,
            UserAnimeStatus.OnHold => HistoryActionType.OnHold,
            _ => HistoryActionType.AddedToList
        };

        var result = await SaveAndPublishAsync(item, draft, [(actionType, null)], progressChanged: false,
            "Added locally: no active tracker accounts connected", ct);
        return generation.HasValue ? WithCommittedEdit(result, draft) : result;
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

        if (originalItem.Id != updatedItem.Id || originalItem.MediaKind != updatedItem.MediaKind)
        {
            return ListActionResult.Fail("Mismatched anime IDs");
        }

        if (updatedItem.Status == UserAnimeStatus.None)
        {
            return await RemoveFromListAsync(originalItem.Id, originalItem.MediaKind, ct);
        }

        using var write = _entryWriter != null ? await _entryWriter.AcquireWriteAsync(ct) : null;
        var draft = originalItem.Clone();
        updatedItem.CopyTo(draft);
        return await PersistEditAsync(originalItem, draft, null, ct);
    }

    private async Task<ListActionResult> PersistEditAsync(
        AnimeEntity originalItem, AnimeEntity draft, AnimeEditFields? editorBaseline, CancellationToken ct)
    {
        bool isManga = originalItem.MediaKind != MediaKind.Anime;
        int oldProgress = isManga ? originalItem.ChaptersRead : originalItem.Progress;
        int newProgress = isManga ? draft.ChaptersRead : draft.Progress;
        bool progressChanged = oldProgress != newProgress;

        bool markedAsDropped = originalItem.Status != UserAnimeStatus.Dropped && draft.Status == UserAnimeStatus.Dropped;
        bool markedAsCompleted = originalItem.Status != UserAnimeStatus.Completed && draft.Status == UserAnimeStatus.Completed;
        bool statusChanged = originalItem.Status != draft.Status;

        if (statusChanged &&
            (draft.Status == UserAnimeStatus.Watching || draft.Status == UserAnimeStatus.Completed) &&
            AppConstants.AiringStatus.IsNotYetAired(draft.StatusDetailed))
        {
            Log.Warning("AnimeListActionService: Cannot set {Title} to {Status} - it has not aired yet.", draft.Title, draft.Status);
            return ListActionResult.Fail($"Cannot set '{draft.Title}' to {draft.Status} because it has not aired yet.");
        }

        bool scoreChanged = originalItem.UserScore != draft.UserScore;

        if (editorBaseline == null || statusChanged)
        {
            var selected = AnimeEditFields.From(draft);
            _mutations.StampStatusDates(draft, markedAsCompleted ? UserAnimeStatus.Completed : null,
                isStarting: draft.Status == UserAnimeStatus.Watching);
            if (editorBaseline is { } baseline)
            {
                if (selected.DateStarted != baseline.DateStarted || originalItem.DateStarted != baseline.DateStarted)
                    draft.DateStarted = selected.DateStarted;
                if (selected.DateCompleted != baseline.DateCompleted || originalItem.DateCompleted != baseline.DateCompleted)
                    draft.DateCompleted = selected.DateCompleted;
                if (selected.IsRewatching != baseline.IsRewatching || originalItem.IsRewatching != baseline.IsRewatching)
                    draft.IsRewatching = selected.IsRewatching;
            }
        }

        var history = new List<(HistoryActionType Action, string? Detail)>();
        if (markedAsCompleted)
        {
            history.Add((HistoryActionType.Completed, null));
        }
        else
        {
            if (progressChanged)
                history.Add((newProgress > oldProgress ? HistoryActionType.Watched : HistoryActionType.Reverted, null));

            if (markedAsDropped)
            {
                history.Add((HistoryActionType.Dropped, null));
            }
            else if (statusChanged && !(progressChanged && newProgress > oldProgress && draft.Status == UserAnimeStatus.Watching))
            {
                var statusAction = draft.Status switch
                {
                    UserAnimeStatus.Watching => newProgress == 0 ? HistoryActionType.AddedToList : HistoryActionType.Watched,
                    UserAnimeStatus.PlanToWatch => HistoryActionType.PlanToWatch,
                    UserAnimeStatus.OnHold => HistoryActionType.OnHold,
                    _ => HistoryActionType.AddedToList
                };
                history.Add((statusAction, null));
            }
        }

        if (scoreChanged)
            history.Add((HistoryActionType.ScoreSet, draft.Score));

        return await SaveAndPublishAsync(originalItem, draft, history, progressChanged,
            "Saved locally: no active tracker accounts connected", ct);
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

    public Task<ListActionResult> RemoveFromListAsync(
        int animeId,
        MediaKind mediaKind,
        CancellationToken ct = default) =>
        RemoveFromListAsync(animeId, mediaKind, null, ct);

    public Task<ListActionResult> RemoveFromEditorAsync(
        int id, MediaKind kind, long generation, CancellationToken ct = default) =>
        RemoveFromListAsync(id, kind, generation, ct);

    private async Task<ListActionResult> RemoveFromListAsync(
        int animeId, MediaKind mediaKind, long? generation, CancellationToken ct)
    {
        if (animeId <= 0) return ListActionResult.Fail("Invalid anime ID");

        if (_entryWriter is { } writer)
        {
            using var write = await writer.AcquireWriteAsync(ct);
            var item = await _mutations.FindExistingAsync(animeId, mediaKind, ct);
            if (generation is { } expected && (item == null || !HasGeneration(animeId, mediaKind, expected)))
                return EntryChanged();
            try
            {
                var hasDestinations = await writer.RemoveAsync(animeId, mediaKind, item, ct);
                _eventBus.Publish(new AnimeListRefreshMessage());
                return ListActionResult.Ok(hasDestinations);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error(ex, "Could not remove library entry {Id} ({MediaKind})", animeId, mediaKind);
                return ListActionResult.Fail("Could not remove the library entry");
            }
        }

        if (generation is { } fallback && !HasGeneration(animeId, mediaKind, fallback))
            return EntryChanged();

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
