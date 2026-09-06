using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Utils.Collections;

namespace Kiriha.Benchmarks.Collections;

[MemoryDiagnoser]
[ShortRunJob]
public class StartupEntityLoadingBenchmark
{
    private List<AnimeEntity> _dbItems = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dbItems = CreateSampleEntities(1000);
    }

    private static List<AnimeEntity> CreateSampleEntities(int count)
    {
        var list = new List<AnimeEntity>(count);
        for (int i = 0; i < count; i++)
        {
            var entity = new AnimeEntity
            {
                Id = i + 1,
                Title = $"Anime Title {i + 1}",
                EnglishTitle = $"English Title {i + 1}",
                RussianTitle = $"Русское название {i + 1}",
                JapaneseTitle = $"日本語 {i + 1}",
                Status = UserAnimeStatus.Watching,
                MediaKind = MediaKind.Anime,
                Progress = i % 24,
                TotalEpisodes = 24,
                Score = "8",
                Type = AppConstants.AnimeTypes.Tv,
                EpisodesAired = 24,
                Synopsis = "Sample synopsis text for testing startup memory allocations.",
                RussianSynopsis = "Пример описания для тестирования памяти при старте.",
                StatusDetailed = "finished_airing",
                StartSeason = "winter",
                StartYear = 2026,
                Genres = new List<string> { "Action", "Fantasy", "Adventure" },
                Studios = new List<string> { "Ufotable" },
                AlternativeTitles = new List<string> { "Alt Title 1", "Alt Title 2" }
            };
            list.Add(entity);
        }
        return list;
    }

    private static AnimeEntity LegacyCloneToViewModel(AnimeEntity entity)
    {
        var clone = new AnimeEntity
        {
            Id = entity.Id,
            MediaKind = entity.MediaKind,
            Chapters = entity.Chapters,
            Volumes = entity.Volumes,
            ChaptersRead = entity.ChaptersRead,
            VolumesRead = entity.VolumesRead,
            AiredSourcePriority = entity.AiredSourcePriority,
            Title = entity.Title,
            RussianTitle = entity.RussianTitle,
            Status = entity.Status,
            Progress = entity.Progress,
            TotalEpisodes = entity.TotalEpisodes,
            Score = entity.Score,
            Type = entity.Type,
            EpisodesAired = entity.EpisodesAired,
            Synopsis = entity.Synopsis,
            RussianSynopsis = entity.RussianSynopsis,
            MainPictureUrl = entity.MainPictureUrl,
            LocalPosterPath = entity.LocalPosterPath,
            Nsfw = entity.Nsfw,
            EnglishTitle = entity.EnglishTitle,
            JapaneseTitle = entity.JapaneseTitle,
            AlternativeTitles = new List<string>(entity.AlternativeTitles),
            Genres = new List<string>(entity.Genres),
            Studios = new List<string>(entity.Studios),
            StatusDetailed = entity.StatusDetailed,
            MeanScore = entity.MeanScore,
            Popularity = entity.Popularity,
            Rank = entity.Rank,
            AiringDate = entity.AiringDate,
            StartSeason = entity.StartSeason,
            StartYear = entity.StartYear,
            Rating = entity.Rating,
            Notes = entity.Notes,
            IsRewatching = entity.IsRewatching,
            RewatchCount = entity.RewatchCount,
            DateStarted = entity.DateStarted,
            DateCompleted = entity.DateCompleted,
            BroadcastDay = entity.BroadcastDay,
            BroadcastTime = entity.BroadcastTime,
            LastEpisodeAt = entity.LastEpisodeAt,
            LastEpisodesSync = entity.LastEpisodesSync,
            NextEpisodeAt = entity.NextEpisodeAt
        };
        _ = clone.Presentation; // Force eager presentation allocation
        return clone;
    }

    [Benchmark(Baseline = true)]
    public (BulkObservableCollection<AnimeEntity> Collection, Dictionary<int, AnimeEntity> Index) Legacy_StartupPipeline_WithToViewModel()
    {
        // 1. Simulates legacy ToViewModel cloning all 1,000 items from DB
        var cached = _dbItems.Select(LegacyCloneToViewModel).ToList();

        // 2. Unoptimized collection reset (repeated Items.Add)
        var collection = new BulkObservableCollection<AnimeEntity>();
        foreach (var item in cached)
        {
            collection.Add(item);
        }

        // 3. Un-sized dictionary population
        var idIndex = new Dictionary<int, AnimeEntity>();
        foreach (var item in cached)
        {
            idIndex[item.Id] = item;
        }

        return (collection, idIndex);
    }

    [Benchmark]
    public (BulkObservableCollection<AnimeEntity> Collection, Dictionary<int, AnimeEntity> Index) Optimized_StartupPipeline_DirectNoClone()
    {
        // 1. Direct use of loaded entities (zero ToViewModel duplication)
        var cached = _dbItems;

        // 2. BulkObservableCollection.Reset using AddRange and pre-allocated capacity
        var collection = new BulkObservableCollection<AnimeEntity>();
        collection.Reset(cached);

        // 3. Pre-sized dictionary with indexed loop
        var idIndex = new Dictionary<int, AnimeEntity>(cached.Count);
        for (int i = 0; i < cached.Count; i++)
        {
            var item = cached[i];
            idIndex[item.Id] = item;
        }

        return (collection, idIndex);
    }

    [Benchmark]
    public List<AnimeEntity> EntityCreation_EagerPresentation_1000Items()
    {
        var list = CreateSampleEntities(1000);
        for (int i = 0; i < list.Count; i++)
        {
            _ = list[i].Presentation; // Eager presentation
        }
        return list;
    }

    [Benchmark]
    public List<AnimeEntity> EntityCreation_LazyPresentation_1000Items()
    {
        // Lazy: presentation is not materialized during data load/querying
        return CreateSampleEntities(1000);
    }
}
