using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Mapping;
using Kiriha.Utils.Parsing;

namespace Kiriha.Benchmarks.Mapping;

[MemoryDiagnoser]
[ShortRunJob]
public class RecognitionCacheBenchmark
{
    private readonly List<AnimeEntity> _items;
    private readonly RecognitionCache _populatedCache;
    private readonly string _searchHit;
    private readonly string _searchMiss;

    public RecognitionCacheBenchmark()
    {
        _items = new List<AnimeEntity>(500);
        var sampleTitles = new[]
        {
            "Boku no Hero Academia",
            "Shingeki no Kyojin",
            "Sousou no Frieren",
            "Steins;Gate",
            "Hunter x Hunter",
            "Fullmetal Alchemist: Brotherhood",
            "Bleach: Sennen Kessen-hen",
            "Kimetsu no Yaiba",
            "Jujutsu Kaisen",
            "One Piece"
        };

        for (int i = 0; i < 500; i++)
        {
            var baseTitle = sampleTitles[i % sampleTitles.Length];
            _items.Add(new AnimeEntity
            {
                Id = i + 1,
                Title = $"{baseTitle} Season {i / 10 + 1}",
                RussianTitle = $"Русское название {baseTitle} {i + 1}",
                EnglishTitle = $"English {baseTitle} {i + 1}",
                JapaneseTitle = $"日本語 {baseTitle} {i + 1}",
                Status = UserAnimeStatus.Watching,
                MediaKind = MediaKind.Anime,
                StartYear = 2010 + (i % 15),
                Type = AppConstants.AnimeTypes.Tv,
                AlternativeTitles = new List<string> { $"{baseTitle} Alt {i + 1}" }
            });
        }

        _populatedCache = new RecognitionCache();
        _populatedCache.BuildIndex(_items);

        _searchHit = AnimeStringHelper.Normalize("Sousou no Frieren Season 1");
        _searchMiss = AnimeStringHelper.Normalize("Nonexistent Anime Title That Does Not Exist In Index");
    }

    [Benchmark]
    public RecognitionCache BuildIndex()
    {
        var cache = new RecognitionCache();
        cache.BuildIndex(_items);
        return cache;
    }

    [Benchmark]
    public int Search_Hit()
    {
        return _populatedCache.Search(_searchHit).Count();
    }

    [Benchmark]
    public int Search_Miss()
    {
        return _populatedCache.Search(_searchMiss).Count();
    }

    [Benchmark]
    public void AddMatch()
    {
        _populatedCache.AddMatch(_searchMiss, 99999, 1.0f);
    }
}
