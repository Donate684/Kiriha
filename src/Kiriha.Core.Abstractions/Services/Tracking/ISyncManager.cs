using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public interface ISyncManager
{
    Task EnqueueUpdateAsync(
        int animeId,
        MediaKind mediaKind,
        int progress,
        UserAnimeStatus? status = null,
        int? score = null,
        string? targetTracker = null);

    Task EnqueueRemoveAsync(int animeId, MediaKind mediaKind);

    Task EnqueueFullUpdateAsync(AnimeEntity item);
    Task<bool> CommitProgressChangeAsync(
        AnimeEntity item, UserAnimeStatus? nextStatus, HistoryItem history, bool onlyAdvance, CancellationToken ct);

    Task<HistoryRetryResult> RetryHistoryDeliveryAsync(int historyId, string trackerName, CancellationToken ct = default);
}

public enum HistoryRetryResult
{
    NotRetryable,
    AccountUnavailable,
    Queued
}
