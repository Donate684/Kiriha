using Kiriha.Core.Domain.Models;

namespace Kiriha.Core.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for the user-action history (the <c>history</c> table).
/// Append-only from the caller's perspective — purging old entries is the
/// responsibility of <see cref="DatabaseMaintenance"/>, not this repo.
/// </summary>
public interface IHistoryRepository
{
    Task AddAsync(HistoryItem item, CancellationToken ct = default);

    /// <summary>Most recent <paramref name="limit"/> entries, newest first.</summary>
    Task<List<HistoryItem>> GetAsync(int limit = 1000, CancellationToken ct = default);

    Task UpdateAsync(HistoryItem item, CancellationToken ct = default);
    Task UpdateTrackerStatusAsync(int animeId, int? episode, string trackerName, TrackerSyncState state, string? error = null, CancellationToken ct = default);
    Task SetPendingTrackersAsync(int animeId, int? episode, IEnumerable<string> trackerNames, CancellationToken ct = default);
}

