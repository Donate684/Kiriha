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
                // Self-healing 1: DB has "JP", but authoritative API or detector proves "CN" or "KR"
                var firmForeign = !string.IsNullOrEmpty(item.CountryOfOrigin) && item.CountryOfOrigin != "JP"
                    ? item.CountryOfOrigin
                    : localDetected;

                if (firmForeign != null && dbCountry == "JP")
                {
                    item.CountryOfOrigin = firmForeign;
                    newlyResolved[item.Id] = firmForeign;
                    updatedAny = true;
                    continue;
                }

                // Self-healing 2: DB has false "CN" (or "KR"), but API is "JP" OR (detector is null and JapaneseTitle has Kana)
                if (dbCountry != "JP" &&
                    (item.CountryOfOrigin == "JP" ||
                     (localDetected == null && !string.IsNullOrEmpty(item.JapaneseTitle) && DonghuaDetector.ContainsKana(item.JapaneseTitle))))
                {
                    item.CountryOfOrigin = "JP";
                    newlyResolved[item.Id] = "JP";
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

            // 2. Not in DB -> prioritize authoritative API country, then local detector, fallback "JP"
            string country = !string.IsNullOrWhiteSpace(item.CountryOfOrigin)
                ? item.CountryOfOrigin
                : (localDetected ?? "JP");
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
