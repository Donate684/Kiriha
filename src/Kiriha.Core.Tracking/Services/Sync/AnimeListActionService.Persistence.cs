using Kiriha.Core.Abstractions.Messages;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Serilog;

namespace Kiriha.Core.Tracking.Sync;

public partial class AnimeListActionService
{
    public async Task<ListActionResult> SaveEditorChangesAsync(
        int id, MediaKind kind, long generation, AnimeEditFields baseline, AnimeEditFields proposed,
        CancellationToken ct = default)
    {
        using var write = _entryWriter != null ? await _entryWriter.AcquireWriteAsync(ct) : null;
        var latest = await _mutations.FindExistingAsync(id, kind, ct);
        if (latest == null || !HasGeneration(id, kind, generation))
            return EntryChanged();

        var current = AnimeEditFields.From(latest);
        if (!baseline.TryMerge(proposed, current, out var merged))
            return ListActionResult.Fail("The same fields were changed in the library") with
            {
                Conflict = AnimeEditConflict.FieldsChanged
            };

        if (merged == current)
            return WithCommittedEdit(ListActionResult.Ok(CheckHasActiveTrackers()), latest);

        if (merged.Status == UserAnimeStatus.None)
            return ListActionResult.Fail("Use the remove command to delete a library entry");

        var draft = latest.Clone();
        merged.ApplyTo(draft);
        if (current.Notes != null && draft.Notes == null)
            draft.Notes = string.Empty;

        var result = await PersistEditAsync(latest, draft, baseline, ct);
        return WithCommittedEdit(result, draft);
    }

    private bool HasGeneration(int id, MediaKind kind, long expected) =>
        _animeRepository.GetEntryGeneration(id, kind) == expected;

    private static ListActionResult EntryChanged() => ListActionResult.Fail("The library entry was replaced or removed") with
    {
        Conflict = AnimeEditConflict.EntryChanged
    };

    private ListActionResult WithCommittedEdit(ListActionResult result, AnimeEntity item) => result.Success
        ? result with { CommittedEdit = (AnimeEditFields.From(item), _animeRepository.GetEntryGeneration(item.Id, item.MediaKind)) }
        : result;

    private async Task<ListActionResult> SaveAndPublishAsync(
        AnimeEntity original, AnimeEntity draft, IReadOnlyList<(HistoryActionType Action, string? Detail)> actions,
        bool progressChanged, string localMessage, CancellationToken ct)
    {
        var progress = draft.MediaKind == MediaKind.Anime ? draft.Progress : draft.ChaptersRead;
        bool hasDestinations;
        if (_entryWriter is { } writer)
        {
            var history = actions.Count == 0
                ? [writer.CreateHistory(draft, HistoryActionType.Edited)]
                : actions.Select(action => writer.CreateHistory(draft, action.Action, action.Detail)).ToArray();
            try
            {
                hasDestinations = await writer.SaveAsync(draft, history, ct, original);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error(ex, "Could not save library entry {Id} ({MediaKind})", draft.Id, draft.MediaKind);
                return ListActionResult.Fail("Could not save the library entry");
            }
        }
        else
        {
            await _uiDispatcher.InvokeAsync(() => draft.CopyTo(original));
            await _animeRepository.AddOrUpdateAnimeAsync(original);
            foreach (var action in actions)
            {
                await _historyService.AddEntryAsync(original.Id, original.MediaKind, original.Title,
                    original.RussianTitle, progress, action.Action, action.Detail, original.MainPictureUrl, ct);
            }

            hasDestinations = CheckHasActiveTrackers();
            if (hasDestinations)
                await _syncManager.EnqueueFullUpdateAsync(original);
        }

        if (progressChanged)
            await _uiDispatcher.InvokeAsync(() => _eventBus.Publish(new AnimeProgressChangedMessage(original, progress)));
        _eventBus.Publish(new AnimeListRefreshMessage());
        if (!hasDestinations)
            _notificationService?.NotifySyncFailed(original.Title, "Trackers", localMessage, willRetry: false);
        TriggerAiringSyncIfOngoing(original);
        return ListActionResult.Ok(hasDestinations, hasDestinations ? null : localMessage);
    }
}
