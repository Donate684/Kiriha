using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models;

namespace Kiriha.Core.Repositories;

/// <summary>
/// Persistence boundary for the user-action history (the <c>history</c> table).
/// Append-only from the caller's perspective â€” purging old entries is the
/// responsibility of <see cref="DatabaseMaintenance"/>, not this repo.
/// </summary>
public interface IHistoryRepository
{
    Task AddAsync(HistoryItem item);

    /// <summary>Most recent <paramref name="limit"/> entries, newest first.</summary>
    Task<List<HistoryItem>> GetAsync(int limit = 1000);
}
