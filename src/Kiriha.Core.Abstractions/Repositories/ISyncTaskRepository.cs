using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for the offline-replay queue of tracker mutations
/// (the <c>sync_tasks</c> table). <see cref="Services.Api.SyncManager"/> drains
/// these on app start and on a 30-second loop; failed pushes stay here until
/// they hit the retry cap, at which point <see cref="DatabaseMaintenance"/>
/// converts them into <c>SyncFailed</c> history entries.
/// </summary>
public interface ISyncTaskRepository
{
    /// <summary>Persists a new task and returns its assigned id.</summary>
    Task<int> AddAsync(SyncTaskEntity task, CancellationToken ct = default);

    /// <summary>All currently queued tasks, ordered by id ascending (FIFO).</summary>
    Task<List<SyncTaskEntity>> GetPendingAsync(CancellationToken ct = default);

    Task UpdateAsync(SyncTaskEntity task, CancellationToken ct = default);

    /// <summary>Idempotent: a concurrent removal is treated as success.</summary>
    Task RemoveAsync(int id, CancellationToken ct = default);

    Task RemoveManyAsync(IEnumerable<int> ids, CancellationToken ct = default);

    Task RemoveForAnimeAsync(int animeId, CancellationToken ct = default);
}
