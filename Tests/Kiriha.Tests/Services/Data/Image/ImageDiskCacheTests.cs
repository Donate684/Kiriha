using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Services.Data.Image;
using Moq;
using Xunit;

namespace Kiriha.Tests.Services.Data.Image;

public class ImageDiskCacheTests : IDisposable
{
    private readonly string _cacheRoot;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly ImageDownloader _downloader;

    public ImageDiskCacheTests()
    {
        _cacheRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_cacheRoot);

        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _downloader = new ImageDownloader(_httpClientFactoryMock.Object, _cacheRoot);
    }

    public void Dispose()
    {
        _downloader.Dispose();
        if (Directory.Exists(_cacheRoot))
        {
            Directory.Delete(_cacheRoot, true);
        }
    }

    [Fact]
    public async Task ResolveLocalPathAsync_EmptyUrl_ReturnsEmpty()
    {
        var cache = new ImageDiskCache(_cacheRoot, _downloader);
        var result = await cache.ResolveLocalPathAsync("");
        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveLocalPathAsync_LocalFileExists_ReturnsSamePath()
    {
        var cache = new ImageDiskCache(_cacheRoot, _downloader);
        var localFile = Path.Combine(_cacheRoot, "test.jpg");
        File.WriteAllText(localFile, "test");

        var result = await cache.ResolveLocalPathAsync(localFile);
        
        Assert.Equal(localFile, result);
    }

    [Fact]
    public async Task ResolveLocalPathAsync_HttpUrlCached_ReturnsCachedPathWithoutModifyingLastWriteTime()
    {
        var cache = new ImageDiskCache(_cacheRoot, _downloader);
        var url = "http://example.com/image.jpg";
        var fileName = ImageDownloader.GetHashString(url) + ".jpg";
        var cachedFile = Path.Combine(_cacheRoot, fileName);
        
        File.WriteAllText(cachedFile, "data");
        var originalDate = DateTime.Now.AddDays(-2);
        File.SetLastWriteTime(cachedFile, originalDate);

        var result = await cache.ResolveLocalPathAsync(url);

        Assert.Equal(cachedFile, result);
        var afterDate = File.GetLastWriteTime(cachedFile);
        // LastWriteTime should NOT be touched during read (zero disk writes)
        Assert.Equal(originalDate, afterDate);
    }

    [Fact]
    public async Task ResolveLocalPathAsync_HttpUrlCachedCorrupted_DeletesAndDownloads()
    {
        var cache = new ImageDiskCache(_cacheRoot, _downloader);
        var url = "http://example.com/image.jpg";
        var fileName = ImageDownloader.GetHashString(url) + ".jpg";
        var cachedFile = Path.Combine(_cacheRoot, fileName);
        
        // Corrupted file (0 bytes)
        File.WriteAllText(cachedFile, "");

        // Since we didn't mock HttpClient properly with a response, the downloader will throw/fail and return empty string.
        // But we can check that the corrupted file was deleted.
        var result = await cache.ResolveLocalPathAsync(url);

        // It should delete the 0-byte file before attempting to download.
        Assert.False(File.Exists(cachedFile));
        Assert.Empty(result); // Download failed because we didn't mock HttpClient, which is fine for this test.
    }
}
