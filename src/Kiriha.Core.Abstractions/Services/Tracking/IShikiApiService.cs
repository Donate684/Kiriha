using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using System.Threading;
using System.Threading.Tasks;

namespace Kiriha.Core.Abstractions.Services;

public interface IShikiApiService : ITrackerService
{
    Task<ShikiFranchiseResponse?> GetFranchiseAsync(int animeId, CancellationToken ct = default);
    Task<ShikiPersonResponse?> GetPersonWorksAsync(int personId, CancellationToken ct = default);
    Task<EpisodeAiringInfo?> GetAiringInfoAsync(int malId, bool force = false, CancellationToken ct = default);
}
