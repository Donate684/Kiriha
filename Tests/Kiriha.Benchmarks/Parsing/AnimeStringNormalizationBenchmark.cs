using BenchmarkDotNet.Attributes;
using Kiriha.Utils.Parsing;

namespace Kiriha.Benchmarks.Parsing;

[MemoryDiagnoser]
[ShortRunJob]
public class AnimeStringNormalizationBenchmark
{
    private readonly string _simpleTitle;
    private readonly string _complexTitle;
    private readonly string _shikiDescription;

    public AnimeStringNormalizationBenchmark()
    {
        _simpleTitle = "Sousou no Frieren";
        _complexTitle = "[SubsPlease] Boku no Hero Academia - Season 7 - 12 (1080p) [HEVC x265] [F1A2B3C4].mkv";
        _shikiDescription = "[b]Главный герой[/b] сериала — [character=123]Имя[/character]. Описание содержит [spoiler]секретную информацию[/spoiler] и ссылки [url=https://example.com]тут[/url].";
    }

    [Benchmark]
    public string Normalize_ComplexTitle_Hit()
    {
        // Hit cache
        return AnimeStringHelper.Normalize(_complexTitle);
    }

    [Benchmark]
    public string Normalize_SimpleTitle_Hit()
    {
        return AnimeStringHelper.Normalize(_simpleTitle);
    }

    [Benchmark]
    public string CleanShikimoriDescription()
    {
        return AnimeStringHelper.CleanShikiDescription(_shikiDescription);
    }
}
