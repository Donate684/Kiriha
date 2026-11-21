using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public interface IMalApiService : ITrackerService
{
    Task<(AnimeEntity? Details, bool IsFresh)> ReadCachedDetailsAsync(int id, MediaKind kind, CancellationToken ct = default) =>
        Task.FromResult<(AnimeEntity?, bool)>((null, false));

    /// <summary>Revalidates a displayed cache entry and returns metadata after it changes.</summary>
    Task<AnimeEntity?> RefreshCachedDetailsAsync(int id, MediaKind kind, CancellationToken ct = default) =>
        Task.FromResult<AnimeEntity?>(null);

    Task<System.Collections.Generic.List<AnimeEntity>> GetSeasonalAnimeAsync(int year, string season, System.Threading.CancellationToken ct = default);
}
