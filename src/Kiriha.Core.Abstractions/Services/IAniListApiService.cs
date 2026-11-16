using Kiriha.Core.Domain.Models;

namespace Kiriha.Core.Abstractions.Services;

public interface IAniListApiService : ITrackerService
{
    Task<AniListAiringInfo?> GetNextAiringAsync(int malId, bool force = false, CancellationToken ct = default);
}
