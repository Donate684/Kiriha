using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Kiriha.Core;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Benchmarks.Collections;

[MemoryDiagnoser]
[ShortRunJob]
public class AnimeListSortingBenchmark
{
    private readonly List<AnimeEntity> _items;

    public AnimeListSortingBenchmark()
    {
        _items = new List<AnimeEntity>(1000);
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

        var now = DateTime.UtcNow;
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
                Score = sampleScores[i % sampleScores.Length],
                Rating = (i % 20 == 0) ? "rx" : "pg_13",
                Progress = i % 25,
                TotalEpisodes = 25,
                Type = AppConstants.AnimeTypes.Tv,
                LastEpisodeAt = (i % 5 == 0) ? now.AddHours(-12) : now.AddDays(-10),
                NextEpisodeAt = (i % 4 == 0) ? now.AddHours(-2) : now.AddDays(3),
                EpisodesAired = 12
            };
            _items.Add(item);
        }
    }

    [Benchmark(Baseline = true)]
    public List<AnimeEntity> Linq_SortByScore_PrioritizeNewEpisodes()
    {
        return _items.ApplySorting("Score", isSeasonal: false, prioritizeNewEpisodes: true).ToList();
    }

    [Benchmark]
    public List<AnimeEntity> Optimized_ListSortByScore_PrioritizeNewEpisodes()
    {
        var copy = new List<AnimeEntity>(_items);
        copy.Sort(ScoreWithNewEpisodeComparer.Instance);
        return copy;
    }

    [Benchmark]
    public List<AnimeEntity> Linq_SortByTitle()
    {
        return _items.ApplySorting("Title", isSeasonal: false, prioritizeNewEpisodes: false).ToList();
    }

    [Benchmark]
    public List<AnimeEntity> Optimized_ListSortByTitle()
    {
        var copy = new List<AnimeEntity>(_items);
        copy.Sort(TitleComparer.Instance);
        return copy;
    }

    private sealed class ScoreWithNewEpisodeComparer : IComparer<AnimeEntity>
    {
        public static readonly ScoreWithNewEpisodeComparer Instance = new();

        public int Compare(AnimeEntity? x, AnimeEntity? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return 1;
            if (y is null) return -1;

            // 1. Badge priority (HasNewEpisodeBadge descending)
            int badgeCompare = y.Presentation.HasNewEpisodeBadge.CompareTo(x.Presentation.HasNewEpisodeBadge);
            if (badgeCompare != 0) return badgeCompare;

            // 2. Score value descending
            return y.ScoreValue.CompareTo(x.ScoreValue);
        }
    }

    private sealed class TitleComparer : IComparer<AnimeEntity>
    {
        public static readonly TitleComparer Instance = new();

        public int Compare(AnimeEntity? x, AnimeEntity? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return 1;
            if (y is null) return -1;

            return string.Compare(x.Title, y.Title, StringComparison.Ordinal);
        }
    }
}
