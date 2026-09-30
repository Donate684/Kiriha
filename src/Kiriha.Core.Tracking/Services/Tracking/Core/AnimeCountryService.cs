using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Serilog;

namespace Kiriha.Core.Tracking.Services;

public sealed class AnimeCountryService : IAnimeCountryService
{
    private readonly IAnimeCountryRepository _countryRepo;

    public AnimeCountryService(IAnimeCountryRepository countryRepo)
    {
        _countryRepo = countryRepo;
    }

    public async Task<bool> HydrateCountriesAsync(IReadOnlyList<AnimeEntity> items, CancellationToken ct = default)
    {
        if (items == null || items.Count == 0) return false;

        bool updatedAny = false;
        var newlyResolved = new Dictionary<int, string>();
        var itemIds = items.Select(x => x.Id).Distinct().ToList();

        // 1. Check local DB first
        var cached = await _countryRepo.GetBatchAsync(itemIds, ct);

        foreach (var item in items)
        {
            // Local detector (0ms, 100% offline)
            var localDetected = DonghuaDetector.Detect(item);

            if (cached.TryGetValue(item.Id, out var dbCountry))
            {
                // Self-healing: if DB was previously saved as "JP", but local detector firmly identifies "CN" or "KR", heal!
                if (localDetected != null && dbCountry == "JP")
                {
                    item.CountryOfOrigin = localDetected;
                    newlyResolved[item.Id] = localDetected;
                    updatedAny = true;
                    continue;
                }

                if (item.CountryOfOrigin != dbCountry)
                {
                    item.CountryOfOrigin = dbCountry;
                    updatedAny = true;
                }
                continue;
            }

            // 2. Not in DB -> evaluate via local detector
            string country = localDetected ?? "JP";
            item.CountryOfOrigin = country;
            newlyResolved[item.Id] = country;
            updatedAny = true;
        }

        // 3. Persist resolved items to SQLite
        if (newlyResolved.Count > 0)
        {
            await _countryRepo.UpsertBatchAsync(newlyResolved, ct);
        }

        return updatedAny;
    }
}
