using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Services.AppLifecycle;
using Kiriha.Services.AppLifecycle;
using Serilog;

namespace Kiriha.Services.Data.Image;

public class ImageCacheCleanup
{
    private readonly string _cacheRoot;
    private const long MaxDiskCacheSizeBytes = 1024L * 1024 * 1024; // 1 GB

    public ImageCacheCleanup(string cacheRoot)
    {
        _cacheRoot = cacheRoot;
    }

    public void ScheduleStartupCleanup(IBackgroundTaskSupervisor backgroundTasks)
    {
        if (!Directory.Exists(_cacheRoot))
        {
            Directory.CreateDirectory(_cacheRoot);
        }
        else
        {
            backgroundTasks.Run("ImageCacheService.Cleanup", _ => CleanupCacheIfNeededAsync());
        }
    }

    public async Task PerformSmartCleanupAsync(IEnumerable<string> activePaths)
    {
        try
        {
            var activeSet = activePaths
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => Path.GetFileName(p).ToLowerInvariant())
                .ToHashSet();

            var directoryInfo = new DirectoryInfo(_cacheRoot);
            if (!directoryInfo.Exists) return;

            int deletedCount = 0;
            long reclaimedSpace = 0;
            var threshold = DateTime.UtcNow.AddDays(-7);

            await Task.Run(() =>
            {
                var snapshot = directoryInfo.EnumerateFiles().ToList();
                foreach (var file in snapshot)
                {
                    if (!activeSet.Contains(file.Name.ToLowerInvariant()))
                    {
                        if (file.LastWriteTime < threshold)
                        {
                            try
                            {
                                long len = file.Length;
                                file.Delete();
                                reclaimedSpace += len;
                                deletedCount++;
                            }
                            catch (Exception ex) { Log.Debug(ex, "File may have been removed concurrently: {FilePath}", file.FullName); }
                        }
                    }
                }
            });

            if (deletedCount > 0)
                Log.Information("ImageCacheCleanup: Cleaned {Count} unreferenced old images, reclaimed {Space:N2} MB",
                    deletedCount, reclaimedSpace / 1024.0 / 1024.0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ImageCacheCleanup: Error during smart cleanup");
        }
    }

    private async Task CleanupCacheIfNeededAsync()
    {
        try
        {
            var directoryInfo = new DirectoryInfo(_cacheRoot);
            if (!directoryInfo.Exists) return;

            var files = directoryInfo.EnumerateFiles().ToList();
            long totalSize = files.Sum(f => f.Length);

            if (totalSize > MaxDiskCacheSizeBytes)
            {
                long targetSize = (long)(MaxDiskCacheSizeBytes * 0.7);
                var filesToDelete = files.OrderBy(f => f.LastWriteTime).ToList();

                foreach (var file in filesToDelete)
                {
                    if (totalSize <= targetSize) break;
                    long len = file.Length;
                    try
                    {
                        file.Delete();
                        totalSize -= len;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Failed to delete file {FilePath} during cleanup", file.FullName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Cache cleanup failed");
        }
    }
}
