using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Library;

namespace Kiriha.Core.Tracking.Sync;

/// <summary>Commits list actions before changing the observable library.</summary>
public sealed class LibraryEntryWriter(
    AnimeRepository library, SyncManager sync, LibrarySyncGate gate, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    internal Task<IDisposable> AcquireWriteAsync(CancellationToken ct) => gate.AcquireProgressWriteAsync(ct);

    internal async Task<bool> SaveAsync(
        AnimeEntity item, IReadOnlyList<HistoryItem> history, CancellationToken ct, AnimeEntity? displayedItem = null)
    {
        var hasDestinations = await sync.CommitEntryChangeAsync(item.Id, item.MediaKind, item, history, ct);
        gate.RecordProgressChange(item.Id, item.MediaKind);
        await library.PublishSavedEntryAsync(displayedItem ?? item, displayedItem == null ? null : item);
        return hasDestinations;
    }

    internal async Task<bool> RemoveAsync(int id, MediaKind kind, AnimeEntity? item, CancellationToken ct)
    {
        HistoryItem[] history = item is null ? [] : [CreateHistory(item, HistoryActionType.Deleted)];
        var hasDestinations = await sync.CommitEntryChangeAsync(id, kind, null, history, ct);
        gate.RecordEntryRemoval(id, kind);
        await library.PublishRemovedEntryAsync(id, kind);
        return hasDestinations;
    }

    internal HistoryItem CreateHistory(AnimeEntity item, HistoryActionType action, string? detail = null) => new()
    {
        AnimeId = item.Id,
        MediaKind = item.MediaKind,
        AnimeTitle = item.Title,
        RussianTitle = item.RussianTitle,
        Episode = item.MediaKind == MediaKind.Anime ? item.Progress : item.ChaptersRead,
        Timestamp = _clock.GetUtcNow().UtcDateTime,
        ActionType = (int)action,
        Detail = detail ?? string.Empty,
        PosterUrl = item.MainPictureUrl
    };
}
