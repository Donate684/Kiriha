using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public interface IMalApiService : ITrackerService
{
    Task<System.Collections.Generic.List<AnimeEntity>> GetSeasonalAnimeAsync(int year, string season, System.Threading.CancellationToken ct = default);
}
