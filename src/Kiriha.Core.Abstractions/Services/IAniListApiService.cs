using Kiriha.Core.Domain.Models;

namespace Kiriha.Core.Abstractions.Services;

public interface IAniListApiService
{
    Task<AniListAiringInfo?> GetNextAiringAsync(int malId, bool force = false, CancellationToken ct = default);
    Task<Dictionary<int, string>> GetCountriesBatchAsync(IReadOnlyList<int> malIds, CancellationToken ct = default);
}
