using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for the offline-replay queue of tracker mutations
/// (the <c>sync_tasks</c> table).
/// </summary>
public interface ISyncTaskRepository
{
    /// <summary>Persists a new task and returns its assigned id.</summary>
    Task<int> AddAsync(SyncTaskEntity task, CancellationToken ct = default);

    /// <summary>Persists all destinations for a mutation atomically and populates their assigned ids.</summary>
    Task AddRangeAsync(IReadOnlyList<SyncTaskEntity> tasks, CancellationToken ct = default);

    /// <summary>All currently queued tasks, ordered by id ascending (FIFO).</summary>
    Task<List<SyncTaskEntity>> GetPendingAsync(CancellationToken ct = default);

    Task UpdateAsync(SyncTaskEntity task, CancellationToken ct = default);

    Task<HistoryItem?> GetHistoryEntryAsync(int historyId, CancellationToken ct);

    Task<HistoryItem?> QueueHistoryRetryAsync(int historyId, string trackerName, SyncTaskEntity task, CancellationToken ct);

    Task<HistoryItem?> CheckpointDeliveryAsync(
        SyncTaskEntity task, string trackerName, TrackerSyncState state, string? error, CancellationToken ct);

    /// <summary>Idempotent: a concurrent removal is treated as success.</summary>
    Task RemoveAsync(int id, CancellationToken ct = default);

    Task RemoveManyAsync(IEnumerable<int> ids, CancellationToken ct = default);
}
