using Kiriha.Models.Api;
using System.Threading;
using System.Threading.Tasks;

namespace Kiriha.Core.Services;

public interface IShikiApiService : ITrackerService
{
    Task<ShikiFranchiseResponse?> GetFranchiseAsync(int animeId, CancellationToken ct = default);
    Task<ShikiPersonResponse?> GetPersonWorksAsync(int personId, CancellationToken ct = default);
}