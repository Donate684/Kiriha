using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models.Entities;

namespace Kiriha.Services.Data.Repository;

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
    Task<int> AddAsync(SyncTaskEntity task);

    /// <summary>All currently queued tasks, ordered by id ascending (FIFO).</summary>
    Task<List<SyncTaskEntity>> GetPendingAsync();

    Task UpdateAsync(SyncTaskEntity task);

    /// <summary>Idempotent: a concurrent removal is treated as success.</summary>
    Task RemoveAsync(int id);

    Task RemoveManyAsync(IEnumerable<int> ids);

    Task RemoveForAnimeAsync(int animeId);
}
