using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Serilog;

namespace Kiriha.Core.Tracking.Services;

public sealed class AnimeCountryService : IAnimeCountryService
{
    private readonly IAniListApiService _aniListApi;
    private readonly IAnimeCountryRepository _countryRepo;

    public AnimeCountryService(IAniListApiService aniListApi, IAnimeCountryRepository countryRepo)
    {
        _aniListApi = aniListApi;
        _countryRepo = countryRepo;
    }

    public async Task<bool> HydrateCountriesAsync(IReadOnlyList<AnimeEntity> items, CancellationToken ct = default)
    {
        if (items == null || items.Count == 0) return false;

        var missing = items
            .Where(x => string.IsNullOrEmpty(x.CountryOfOrigin))
            .ToList();

        if (missing.Count == 0) return false;

        bool updatedAny = false;
        var missingIds = missing.Select(x => x.Id).Distinct().ToList();

        // 1. Check local DB first
        var cached = await _countryRepo.GetBatchAsync(missingIds, ct);
        var stillMissing = new List<AnimeEntity>();

        foreach (var item in missing)
        {
            if (cached.TryGetValue(item.Id, out var country))
            {
                item.CountryOfOrigin = country;
                updatedAny = true;
            }
            else
            {
                stillMissing.Add(item);
            }
        }

        if (stillMissing.Count == 0) return updatedAny;

        // 2. Query AniList GraphQL in batches
        var stillMissingIds = stillMissing.Select(x => x.Id).Distinct().ToList();
        var aniListResults = await _aniListApi.GetCountriesBatchAsync(stillMissingIds, ct);

        var newlyResolved = new Dictionary<int, string>();

        foreach (var item in stillMissing)
        {
            string? country = null;

            if (aniListResults.TryGetValue(item.Id, out var aniCountry))
            {
                country = aniCountry;
            }
            else
            {
                // 3. Fallback heuristic (Studios, Synopsis, Hanzi)
                var detected = DonghuaDetector.Detect(item);
                country = detected ?? "JP";
            }

            item.CountryOfOrigin = country;
            newlyResolved[item.Id] = country;
            updatedAny = true;
        }

        // 4. Persist newly resolved to SQLite
        if (newlyResolved.Count > 0)
        {
            await _countryRepo.UpsertBatchAsync(newlyResolved, ct);
        }

        return updatedAny;
    }
}
