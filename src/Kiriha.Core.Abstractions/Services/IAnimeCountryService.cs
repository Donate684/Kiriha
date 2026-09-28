using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Core.Abstractions.Services;

public interface IAnimeCountryService
{
    Task<bool> HydrateCountriesAsync(IReadOnlyList<AnimeEntity> items, CancellationToken ct = default);
}
