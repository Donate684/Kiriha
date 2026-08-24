using System.Linq;
using BenchmarkDotNet.Attributes;
using Kiriha.Utils.Parsing;

namespace Kiriha.Benchmarks.Parsers;

[MemoryDiagnoser]
[ShortRunJob]
public class AnimeParseCacheBenchmark
{
    private readonly string[] _filenames;

    public AnimeParseCacheBenchmark()
    {
        _filenames = new string[100];
        for (int i = 0; i < 100; i++)
        {
            _filenames[i] = $"[SubsPlease] Boku no Hero Academia - 1{i:D2} (1080p) [F1A2B3C4].mkv";
        }
    }

    [Benchmark(Baseline = true)]
    public void ParseWithoutCache()
    {
        foreach (var filename in _filenames)
        {
            _ = AnitomySharp.AnitomySharp.Parse(filename).ToList();
        }
    }

    [Benchmark]
    public void ParseWithCache()
    {
        // First time will miss, subsequent 4 times will hit cache
        // Because in real world MappingService calls it 4 times per matching attempt.
        for (int j = 0; j < 5; j++)
        {
            foreach (var filename in _filenames)
            {
                _ = AnimeParseCache.Parse(filename);
            }
        }
    }
}
