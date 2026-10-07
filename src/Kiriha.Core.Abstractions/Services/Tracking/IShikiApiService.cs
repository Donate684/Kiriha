using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;

namespace Kiriha.Core.Abstractions.Services;

public interface IShikiApiService : ITrackerService
{
    Task<ShikiFranchiseResponse?> GetFranchiseAsync(int animeId, CancellationToken ct = default);
    Task<EpisodeAiringInfo?> GetAiringInfoAsync(int malId, bool force = false, CancellationToken ct = default);
    Task<Dictionary<int, string>> FetchPostersFromGraphQlAsync(IEnumerable<int> ids, bool isManga, CancellationToken ct = default);
}
