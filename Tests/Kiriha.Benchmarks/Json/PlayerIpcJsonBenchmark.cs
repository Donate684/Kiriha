using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using Kiriha.Core.Domain.Models.Api;

namespace Kiriha.Benchmarks.Json;

[JsonSerializable(typeof(InternalPlayerState))]
internal partial class BenchmarkPlayerIpcJsonContext : JsonSerializerContext
{
}

[MemoryDiagnoser]
[ShortRunJob]
public class PlayerIpcJsonBenchmark
{
    private readonly InternalPlayerState _state;
    private readonly string _json;

    public PlayerIpcJsonBenchmark()
    {
        _state = new InternalPlayerState
        {
            AnimeId = 38000,
            AnimeTitle = "Kimetsu no Yaiba: Yuukaku-hen",
            OriginalTitle = "[SubsPlease] Kimetsu no Yaiba - 01 (1080p) [12345678].mkv",
            Episode = "1",
            Position = 1245.5,
            Duration = 1420.0,
            IsPlaying = true,
            IsClosed = false
        };

        _json = JsonSerializer.Serialize(_state);
    }

    [Benchmark(Baseline = true)]
    public string SerializeReflection()
    {
        return JsonSerializer.Serialize(_state);
    }

    [Benchmark]
    public string SerializeSourceGen()
    {
        return JsonSerializer.Serialize(_state, BenchmarkPlayerIpcJsonContext.Default.InternalPlayerState);
    }

    [Benchmark]
    public InternalPlayerState? DeserializeReflection()
    {
        return JsonSerializer.Deserialize<InternalPlayerState>(_json);
    }

    [Benchmark]
    public InternalPlayerState? DeserializeSourceGen()
    {
        return JsonSerializer.Deserialize(_json, BenchmarkPlayerIpcJsonContext.Default.InternalPlayerState);
    }
}
