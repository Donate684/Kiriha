using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Mapping;
using Kiriha.Utils.Parsing;

namespace Kiriha.Benchmarks.Mapping;

[MemoryDiagnoser]
[ShortRunJob]
public class UserListIndexBenchmark
{
    private readonly List<AnimeEntity> _userList;
    private readonly MappingService.UserListIndex _prebuiltIndex;
    private readonly WeakReference<IEnumerable<AnimeEntity>> _cachedRef;
    private readonly string _queryTitle;

    public UserListIndexBenchmark()
    {
        _userList = new List<AnimeEntity>(1000);
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
                Status = UserAnimeStatus.Watching,
                MediaKind = MediaKind.Anime,
                Progress = i % 25,
                TotalEpisodes = 25,
                Type = AppConstants.AnimeTypes.Tv
            };
            _userList.Add(item);
        }

        _prebuiltIndex = MappingService.UserListIndex.Build(_userList);
        _cachedRef = new WeakReference<IEnumerable<AnimeEntity>>(_userList);
        _queryTitle = _userList[500].Title;
    }

    [Benchmark(Baseline = true)]
    public MappingService.UserListIndex Uncached_RebuildEveryTick()
    {
        // Legacy: .ToList() creates a new reference on every 2-second detection tick,
        // defeating ReferenceEquals and forcing a full rebuild of 3 dictionaries across 1,000 items.
        return MappingService.UserListIndex.Build(_userList);
    }

    [Benchmark]
    public MappingService.UserListIndex Cached_ReuseAcrossTicks()
    {
        // Optimized: AnimeRepository.GetSnapshotAsync() caches the instance until collection changes.
        // ReferenceEquals succeeds and returns the prebuilt index instantly with 0 allocations.
        if (_cachedRef.TryGetTarget(out var target) && ReferenceEquals(target, _userList))
        {
            return _prebuiltIndex;
        }

        return MappingService.UserListIndex.Build(_userList);
    }

    [Benchmark]
    public AnimeEntity? EndToEndScrobbleLookup_Uncached()
    {
        // Full pipeline simulation: Rebuild + FindExact
        var index = MappingService.UserListIndex.Build(_userList);
        return index.FindExact(_queryTitle, null, (a, ep) => true);
    }

    [Benchmark]
    public AnimeEntity? EndToEndScrobbleLookup_CachedSnapshot()
    {
        // Full pipeline simulation: Cached index hit + FindExact
        MappingService.UserListIndex index;
        if (_cachedRef.TryGetTarget(out var target) && ReferenceEquals(target, _userList))
        {
            index = _prebuiltIndex;
        }
        else
        {
            index = MappingService.UserListIndex.Build(_userList);
        }

        return index.FindExact(_queryTitle, null, (a, ep) => true);
    }
}
