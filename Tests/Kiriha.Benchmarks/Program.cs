using BenchmarkDotNet.Running;

namespace Kiriha.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<Parsers.AnimeParseCacheBenchmark>();
    }
}
