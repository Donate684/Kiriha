using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Kiriha.Core;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Benchmarks.Collections;

[MemoryDiagnoser]
[ShortRunJob]
public class AnimeCollectionProjectionBenchmark
{
    private readonly List<AnimeEntity> _testItems;
    private readonly AnimeCollectionProjection _projection;

    public AnimeCollectionProjectionBenchmark()
    {
        _testItems = new List<AnimeEntity>(1000);
        var statuses = new[] { UserAnimeStatus.Watching, UserAnimeStatus.Completed, UserAnimeStatus.PlanToWatch, UserAnimeStatus.OnHold, UserAnimeStatus.Dropped };
        var sampleScores = new[] { "8.5", "9.2", "7.14", "-", "10.0", "6.55", "8.0", "9.9", "7.8" };
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

        for (int i = 0; i < 1000; i++)
        {
            var baseTitle = sampleTitles[i % sampleTitles.Length];
            var item = new AnimeEntity
            {
                Id = i + 1,
                Title = $"{baseTitle} Season {i / 10 + 1}",
                RussianTitle = $"Русское название {baseTitle} {i + 1}",
                EnglishTitle = $"English {baseTitle} {i + 1}",
                JapaneseTitle = $"日本語 {baseTitle} {i + 1}",
                Status = statuses[i % statuses.Length],
                MediaKind = MediaKind.Anime,
                Score = sampleScores[i % sampleScores.Length],
                Rating = (i % 20 == 0) ? "rx" : "pg_13",
                Progress = i % 25,
                TotalEpisodes = 25,
                Type = AppConstants.AnimeTypes.Tv
            };
            _testItems.Add(item);
        }

        _projection = new AnimeCollectionProjection();
        _projection.Rebuild(_testItems);
    }

    [Benchmark]
    public List<AnimeEntity> QueryWatching()
    {
        return _projection.Query(UserAnimeStatus.Watching, searchQuery: null, filterNsfw: false, sortBy: "Title", MediaKind.Anime);
    }

    [Benchmark]
    public List<AnimeEntity> QueryWatchingWithSearch()
    {
        return _projection.Query(UserAnimeStatus.Watching, searchQuery: "Hero", filterNsfw: false, sortBy: "Title", MediaKind.Anime);
    }

    [Benchmark]
    public List<AnimeEntity> QuerySortByScore()
    {
        return _projection.Query(UserAnimeStatus.Watching, searchQuery: null, filterNsfw: false, sortBy: "Score", MediaKind.Anime);
    }

    [Benchmark]
    public void Rebuild1000Items()
    {
        _projection.Rebuild(_testItems);
    }
}
