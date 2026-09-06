using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;

namespace Kiriha.Benchmarks.Json;

[MemoryDiagnoser]
[ShortRunJob]
public class SeasonalCacheBenchmark
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly List<AnimeEntity> _sampleItems;
    private readonly string _tempJsonFile;
    private readonly string _tempOutputFile;

    public SeasonalCacheBenchmark()
    {
        _sampleItems = new List<AnimeEntity>(100);
        for (int i = 0; i < 100; i++)
        {
            _sampleItems.Add(new AnimeEntity
            {
                Id = i + 1,
                Title = $"Sample Seasonal Anime Title {i + 1}",
                RussianTitle = $"Пример сезонного аниме {i + 1}",
                EnglishTitle = $"Sample Seasonal Anime English {i + 1}",
                JapaneseTitle = $"サンプルアニメ {i + 1}",
                Status = UserAnimeStatus.Watching,
                MediaKind = MediaKind.Anime,
                Score = "8.2",
                EpisodesAired = 12,
                TotalEpisodes = 12,
                StartSeason = "winter",
                StartYear = 2026,
                Type = AppConstants.AnimeTypes.Tv,
                Genres = new List<string> { "Action", "Fantasy", "Adventure" },
                Studios = new List<string> { "Bones", "Wit Studio" }
            });
        }

        _tempJsonFile = Path.Combine(Path.GetTempPath(), $"seasonal_bench_in_{System.Guid.NewGuid():N}.json");
        _tempOutputFile = Path.Combine(Path.GetTempPath(), $"seasonal_bench_out_{System.Guid.NewGuid():N}.json");

        File.WriteAllText(_tempJsonFile, JsonSerializer.Serialize(_sampleItems, JsonOptions));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempJsonFile)) File.Delete(_tempJsonFile);
        if (File.Exists(_tempOutputFile)) File.Delete(_tempOutputFile);
    }

    [Benchmark(Baseline = true)]
    public List<AnimeEntity>? Load_StringBased()
    {
        var json = File.ReadAllText(_tempJsonFile);
        return JsonSerializer.Deserialize<List<AnimeEntity>>(json, JsonOptions);
    }

    [Benchmark]
    public List<AnimeEntity>? Load_StreamBased()
    {
        using var stream = File.OpenRead(_tempJsonFile);
        return JsonSerializer.Deserialize<List<AnimeEntity>>(stream, JsonOptions);
    }

    [Benchmark]
    public void Save_StringBased()
    {
        var json = JsonSerializer.Serialize(_sampleItems, JsonOptions);
        File.WriteAllText(_tempOutputFile, json);
    }

    [Benchmark]
    public void Save_StreamBased()
    {
        using var stream = File.Create(_tempOutputFile);
        JsonSerializer.Serialize(stream, _sampleItems, JsonOptions);
    }
}
