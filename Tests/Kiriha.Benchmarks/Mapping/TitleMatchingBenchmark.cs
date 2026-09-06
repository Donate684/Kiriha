using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Utils.Parsing;

namespace Kiriha.Benchmarks.Mapping;

[MemoryDiagnoser]
[ShortRunJob]
public class TitleMatchingBenchmark
{
    private readonly List<AnimeEntity> _userList;
    private readonly FrozenDictionary<string, AnimeEntity> _indexedExact;
    private readonly FrozenDictionary<string, AnimeEntity> _indexedNormalized;

    private readonly string _queryExactMatch;
    private readonly string _queryNormalizedMatch;
    private readonly string _queryMiss;

    public TitleMatchingBenchmark()
    {
        _userList = new List<AnimeEntity>(1000);
        var exactDict = new Dictionary<string, AnimeEntity>(StringComparer.OrdinalIgnoreCase);
        var normDict = new Dictionary<string, AnimeEntity>(StringComparer.OrdinalIgnoreCase);

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

            exactDict.TryAdd(item.Title, item);
            if (!string.IsNullOrEmpty(item.EnglishTitle)) exactDict.TryAdd(item.EnglishTitle, item);
            if (!string.IsNullOrEmpty(item.RussianTitle)) exactDict.TryAdd(item.RussianTitle, item);

            normDict.TryAdd(AnimeStringHelper.Normalize(item.Title), item);
            if (!string.IsNullOrEmpty(item.EnglishTitle)) normDict.TryAdd(AnimeStringHelper.Normalize(item.EnglishTitle), item);
            if (!string.IsNullOrEmpty(item.RussianTitle)) normDict.TryAdd(AnimeStringHelper.Normalize(item.RussianTitle), item);
        }

        _indexedExact = exactDict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _indexedNormalized = normDict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        // Target items located in the middle-to-end of the list to simulate realistic search
        var targetExact = _userList[750];
        _queryExactMatch = targetExact.Title;

        var targetNorm = _userList[800];
        _queryNormalizedMatch = targetNorm.Title.ToLowerInvariant().Replace("season", "s");

        _queryMiss = "Completely Non Existent Anime 2026";
    }

    [Benchmark(Baseline = true)]
    public AnimeEntity? LinearScan_ExactMatch()
    {
        return _userList.FirstOrDefault(x =>
            string.Equals(x.Title, _queryExactMatch, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.EnglishTitle, _queryExactMatch, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.RussianTitle, _queryExactMatch, StringComparison.OrdinalIgnoreCase));
    }

    [Benchmark]
    public AnimeEntity? IndexedLookup_ExactMatch()
    {
        _indexedExact.TryGetValue(_queryExactMatch, out var result);
        return result;
    }

    [Benchmark]
    public AnimeEntity? LinearScan_NormalizedMatch()
    {
        string normQuery = AnimeStringHelper.Normalize(_queryNormalizedMatch);
        return _userList.FirstOrDefault(x =>
            AnimeStringHelper.Normalize(x.Title) == normQuery ||
            AnimeStringHelper.Normalize(x.EnglishTitle ?? string.Empty) == normQuery ||
            AnimeStringHelper.Normalize(x.RussianTitle ?? string.Empty) == normQuery);
    }

    [Benchmark]
    public AnimeEntity? IndexedLookup_NormalizedMatch()
    {
        string normQuery = AnimeStringHelper.Normalize(_queryNormalizedMatch);
        _indexedNormalized.TryGetValue(normQuery, out var result);
        return result;
    }

    [Benchmark]
    public AnimeEntity? LinearScan_Miss()
    {
        return _userList.FirstOrDefault(x =>
            string.Equals(x.Title, _queryMiss, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.EnglishTitle, _queryMiss, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.RussianTitle, _queryMiss, StringComparison.OrdinalIgnoreCase));
    }

    [Benchmark]
    public AnimeEntity? IndexedLookup_Miss()
    {
        _indexedExact.TryGetValue(_queryMiss, out var result);
        return result;
    }
}
