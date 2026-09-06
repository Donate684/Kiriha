using System;
using System.Collections.Concurrent;
using System.IO;
using BenchmarkDotNet.Attributes;
using Kiriha.Services.Data.Image;

namespace Kiriha.Benchmarks.Parsing;

[MemoryDiagnoser]
[ShortRunJob]
public class ImageCachePipelineBenchmark
{
    private const string SampleUrl = "https://cdn.myanimelist.net/images/anime/1000/110533.jpg?s=1234567890abcdef";
    private readonly string _tempDir;
    private readonly string _fileName;
    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, string> _urlToPathMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ByteSizedLru<string, object> _memCache = new(1024 * 1024, _ => 1024);
    private readonly object _fakeBitmap = new();

    public ImageCachePipelineBenchmark()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kiriha_bench_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _fileName = ImageDownloader.GetFileNameForUrl(SampleUrl);
        _filePath = Path.Combine(_tempDir, _fileName);
        File.WriteAllBytes(_filePath, new byte[1024]);

        _urlToPathMap[SampleUrl] = _filePath;
        _memCache.Set(_filePath, _fakeBitmap);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Benchmark(Baseline = true)]
    public string Legacy_DiskLookup_WithDiskWrite()
    {
        string fileName = ImageDownloader.GetFileNameForUrl(SampleUrl);
        string candidatePath = Path.Combine(_tempDir, fileName);

        if (File.Exists(candidatePath))
        {
            var fileInfo = new FileInfo(candidatePath);
            if (fileInfo.Length > 0)
            {
                fileInfo.LastWriteTime = DateTime.UtcNow; // Disk write operation
                if (File.Exists(candidatePath)) // Duplicate check in legacy ImageCacheService
                {
                    return candidatePath;
                }
            }
        }
        return string.Empty;
    }

    [Benchmark]
    public string Optimized_DiskLookup_NoDiskWrite()
    {
        string fileName = ImageDownloader.GetFileNameForUrl(SampleUrl);
        string candidatePath = Path.Combine(_tempDir, fileName);

        if (File.Exists(candidatePath))
        {
            var fileInfo = new FileInfo(candidatePath);
            if (fileInfo.Length > 0)
            {
                return candidatePath;
            }
        }
        return string.Empty;
    }

    [Benchmark]
    public object? Optimized_InMemory_FastPath()
    {
        if (_urlToPathMap.TryGetValue(SampleUrl, out var knownPath))
        {
            if (_memCache.TryGet(knownPath, out var cached) && cached != null)
            {
                return cached;
            }
        }
        return null;
    }
}
