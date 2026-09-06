using System;
using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using Kiriha.Services.Data.Image;

namespace Kiriha.Benchmarks.Parsing;

[MemoryDiagnoser]
[ShortRunJob]
public class ImageHashBenchmark
{
    private const string SampleUrl = "https://cdn.myanimelist.net/images/anime/1000/110533.jpg?s=1234567890abcdef";

    [Benchmark(Baseline = true)]
    public string LegacyHash()
    {
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(SampleUrl));
        return Convert.ToHexString(hashBytes);
    }

    [Benchmark]
    public string OptimizedGetHashString()
    {
        return ImageDownloader.GetHashString(SampleUrl);
    }

    [Benchmark]
    public string OptimizedGetFileNameForUrl()
    {
        return ImageDownloader.GetFileNameForUrl(SampleUrl);
    }
}
