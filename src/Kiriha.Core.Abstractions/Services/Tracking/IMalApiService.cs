using Kiriha.Core.Abstractions.Models.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace Kiriha.Core.Services;

public interface IMalApiService : ITrackerService
{
    Task<System.Collections.Generic.List<AnimeEntity>> GetSeasonalAnimeAsync(int year, string season, System.Threading.CancellationToken ct = default);
}
