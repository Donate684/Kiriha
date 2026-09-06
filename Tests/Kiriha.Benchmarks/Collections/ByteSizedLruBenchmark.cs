using BenchmarkDotNet.Attributes;
using Kiriha.Services.Data.Image;

namespace Kiriha.Benchmarks.Collections;

[MemoryDiagnoser]
[ShortRunJob]
public class ByteSizedLruBenchmark
{
    private readonly ByteSizedLru<string, object> _lru;
    private readonly object _val1 = new();
    private readonly object _val2 = new();
    private int _counter;

    public ByteSizedLruBenchmark()
    {
        // 1000 item capacity budget
        _lru = new ByteSizedLru<string, object>(1000 * 1024, _ => 1024);
        for (int i = 0; i < 500; i++)
        {
            _lru.Set($"key_{i}", new object());
        }
        _lru.Set("hot_key", _val1);
    }

    [Benchmark]
    public bool TryGet_HeadItem_Hit()
    {
        return _lru.TryGet("hot_key", out _);
    }

    [Benchmark]
    public bool TryGet_NonHeadItem_Hit()
    {
        return _lru.TryGet("key_100", out _);
    }

    [Benchmark]
    public void Set_ExistingKey_Update()
    {
        _lru.Set("hot_key", _val2);
    }

    [Benchmark]
    public void Set_NewKey_Insert()
    {
        _lru.Set($"dynamic_key_{++_counter % 200}", _val1);
    }
}
