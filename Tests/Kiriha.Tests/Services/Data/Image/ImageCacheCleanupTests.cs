using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using Kiriha.Services.Data.Image;
using Xunit;

namespace Kiriha.Tests.Services.Data.Image;

public class ImageCacheCleanupTests : IDisposable
{
    private readonly string _cacheRoot;

    public ImageCacheCleanupTests()
    {
        _cacheRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_cacheRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
        {
            Directory.Delete(_cacheRoot, true);
        }
    }

    [Fact]
    public async Task PerformSmartCleanupAsync_DeletesInactiveOldFiles()
    {
        var cleanup = new ImageCacheCleanup(_cacheRoot);
        
        var oldFile = Path.Combine(_cacheRoot, "old.jpg");
        File.WriteAllText(oldFile, "test");
        File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-10)); // Older than 7 days

        var newFile = Path.Combine(_cacheRoot, "new.jpg");
        File.WriteAllText(newFile, "test");
        File.SetLastWriteTime(newFile, DateTime.Now.AddDays(-2)); // Newer than 7 days

        await cleanup.PerformSmartCleanupAsync(Array.Empty<string>());

        Assert.False(File.Exists(oldFile)); // Should be deleted
        Assert.True(File.Exists(newFile)); // Should be kept
    }

    [Fact]
    public async Task PerformSmartCleanupAsync_KeepsActiveOldFiles()
    {
        var cleanup = new ImageCacheCleanup(_cacheRoot);
        
        var oldActiveFile = Path.Combine(_cacheRoot, "old_active.jpg");
        File.WriteAllText(oldActiveFile, "test");
        File.SetLastWriteTime(oldActiveFile, DateTime.Now.AddDays(-15)); // Older than 7 days, but active

        await cleanup.PerformSmartCleanupAsync(new[] { oldActiveFile });

        Assert.True(File.Exists(oldActiveFile)); // Should be kept because it's active
    }
}
