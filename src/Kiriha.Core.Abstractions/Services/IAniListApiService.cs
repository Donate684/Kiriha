using Kiriha.Core.Domain.Models;

namespace Kiriha.Core.Abstractions.Services;

public interface IAniListApiService : ITrackerService
{
    Task<EpisodeAiringInfo?> GetNextAiringAsync(int malId, bool force = false, CancellationToken ct = default);

    /// <summary>Reads live schedules. Missing IDs have no confirmed result.</summary>
    Task<IReadOnlyDictionary<int, EpisodeAiringInfo>> GetAiringSchedulesAsync(IReadOnlyList<int> malIds, CancellationToken ct = default);
}
