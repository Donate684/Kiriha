using System.Data.Common;
using System.Text;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Mpv;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Repository;
using Kiriha.Services.Data.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kiriha.Tests;

public class DiskWriteOptimizationTests
{
    [Fact]
    public void SettingsService_SaveWithoutChanges_SkipsDiskWrite()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "KirihaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new SettingsService(tempDir);
            service.SaveImmediate(); // Initial creation

            Assert.True(File.Exists(service.PlayerSettingsPath));
            var writeTime1 = File.GetLastWriteTimeUtc(service.PlayerSettingsPath);

            Thread.Sleep(100);

            // Second save with zero changes -> dirty check must kick in and skip AtomicWrite
            service.SaveImmediate();
            var writeTime2 = File.GetLastWriteTimeUtc(service.PlayerSettingsPath);

            Assert.Equal(writeTime1, writeTime2);

            // Now mutate a setting and save -> must update file
            service.Update(s => s.Player.Volume = (s.Player.Volume == 100 ? 50 : 100), SettingsSection.Player, save: false);
            Thread.Sleep(100);
            service.SaveImmediate();
            var writeTime3 = File.GetLastWriteTimeUtc(service.PlayerSettingsPath);

            Assert.NotEqual(writeTime2, writeTime3);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task SeasonalCacheStore_SaveWithoutChanges_SkipsDiskWrite()
    {
        var store = new SeasonalCacheStore();
        int year = 2088;
        string season = "spring";
        string root = Kiriha.Infrastructure.Platform.PathHelper.GetSeasonalCachePath();
        string cacheFile = Path.Combine(root, $"{year}_{season}.json");

        try
        {
            var items = new List<AnimeEntity>
            {
                new()
                {
                    Id = 88001,
                    Title = "Seasonal Write Test Anime",
                    Status = UserAnimeStatus.Watching,
                    EpisodesAired = 4
                }
            };

            await store.SaveAsync(year, season, items);
            Assert.True(File.Exists(cacheFile));
            var writeTime1 = File.GetLastWriteTimeUtc(cacheFile);

            await Task.Delay(100);

            // Saving identical list must be intercepted by hash dirty check
            await store.SaveAsync(year, season, items);
            var writeTime2 = File.GetLastWriteTimeUtc(cacheFile);

            Assert.Equal(writeTime1, writeTime2);

            // Mutating an item must trigger a fresh write
            items[0].EpisodesAired = 5;
            await Task.Delay(100);
            await store.SaveAsync(year, season, items);
            var writeTime3 = File.GetLastWriteTimeUtc(cacheFile);

            Assert.NotEqual(writeTime2, writeTime3);
        }
        finally
        {
            try { if (File.Exists(cacheFile)) File.Delete(cacheFile); } catch { }
        }
    }

    [Fact]
    public void HttpCacheRepository_CompressesAndDecompressesBody_Transparently()
    {
        // 1. Create a representative API JSON payload
        var jsonText = "{\"data\":[" + string.Join(",", Enumerable.Range(1, 50)
            .Select(i => $"{{\"id\":{i},\"title\":\"Anime Title Number {i}\",\"synopsis\":\"Some repetitive long description text here.\"}}")) + "]}";
        var rawBytes = Encoding.UTF8.GetBytes(jsonText);

        // 2. Test compression
        var compressed = HttpCacheRepository.Compress(rawBytes);
        Assert.True(compressed.Length < rawBytes.Length);
        Assert.True(compressed.Length < rawBytes.Length / 2); // Typically >50% compression

        // Verify Zstandard magic number
        Assert.True(compressed.Length >= 4);
        Assert.Equal(0x28, compressed[0]);
        Assert.Equal(0xB5, compressed[1]);
        Assert.Equal(0x2F, compressed[2]);
        Assert.Equal(0xFD, compressed[3]);

        // 3. Test decompression
        var decompressed = HttpCacheRepository.DecompressIfNeeded(compressed);
        Assert.Equal(rawBytes, decompressed);

        // 4. Test backward compatibility: uncompressed legacy payload is returned as-is
        var legacyPassThrough = HttpCacheRepository.DecompressIfNeeded(rawBytes);
        Assert.Equal(rawBytes, legacyPassThrough);
    }

    [Fact]
    public void HttpCacheRepository_LegacyGZip_StillDecompressesCorrectly()
    {
        string sampleText = "Legacy GZip payload from earlier versions of Kiriha database cache! " + new string('z', 200);
        byte[] raw = Encoding.UTF8.GetBytes(sampleText);

        byte[] gzipCompressed;
        using (var ms = new MemoryStream())
        {
            using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            {
                gz.Write(raw);
            }
            gzipCompressed = ms.ToArray();
        }

        // Verify GZip magic number
        Assert.Equal(0x1F, gzipCompressed[0]);
        Assert.Equal(0x8B, gzipCompressed[1]);

        byte[] decompressed = HttpCacheRepository.DecompressIfNeeded(gzipCompressed);
        Assert.Equal(raw, decompressed);
    }

    [Fact]
    public void SqlitePragmaConnectionInterceptor_AppliesOptimizedPragmasOnOpen()
    {
        var interceptor = new SqlitePragmaConnectionInterceptor();
        using var connection = new SqliteConnection("Data Source=:memory:");
        
        connection.Open();
        interceptor.ConnectionOpened(connection, null!);

        using var cmdSync = connection.CreateCommand();
        cmdSync.CommandText = "PRAGMA synchronous;";
        var syncValue = Convert.ToInt64(cmdSync.ExecuteScalar());
        Assert.Equal(1L, syncValue); // 1 = NORMAL

        using var cmdTemp = connection.CreateCommand();
        cmdTemp.CommandText = "PRAGMA temp_store;";
        var tempValue = Convert.ToInt64(cmdTemp.ExecuteScalar());
        Assert.Equal(2L, tempValue); // 2 = MEMORY
    }

    [Fact]
    public async Task UserAnimeRepository_UpdateProgress_DoesNotInvokeWalCheckpoint()
    {
        var checkpointTracker = new WalCheckpointTrackingInterceptor();

        var factory = new TestDbContextFactory(checkpointTracker);

        // Ensure database tables exist
        using (var ctx = factory.CreateDbContext())
        {
            ctx.Database.OpenConnection();
            ctx.Database.EnsureCreated();

            ctx.UserAnime.Add(new AnimeEntity
            {
                Id = 12345,
                Title = "Checkpoint Test Anime",
                Progress = 1
            });
            ctx.SaveChanges();
        }

        checkpointTracker.Reset();

        var repo = new UserAnimeRepository(factory);
        var entity = new AnimeEntity { Id = 12345, Title = "Checkpoint Test Anime", Progress = 1 };

        await repo.UpdateProgressAsync(entity, 2);

        // Assert: 0 wal_checkpoint calls during progress update
        Assert.Equal(0, checkpointTracker.WalCheckpointCount);
    }

    [Fact]
    public void MpvThumbnailer_BucketCalculation_MatchesExpectedTimeSteps()
    {
        Assert.Equal(0, MpvThumbnailer.GetCacheBucket(0.0));
        Assert.Equal(0, MpvThumbnailer.GetCacheBucket(0.8));
        Assert.Equal(1, MpvThumbnailer.GetCacheBucket(1.5));
        Assert.Equal(1, MpvThumbnailer.GetCacheBucket(2.0));
        Assert.Equal(1, MpvThumbnailer.GetCacheBucket(2.8));
        Assert.Equal(2, MpvThumbnailer.GetCacheBucket(3.5));
        Assert.Equal(2, MpvThumbnailer.GetCacheBucket(4.0));
    }

    private sealed class WalCheckpointTrackingInterceptor : DbCommandInterceptor
    {
        public int WalCheckpointCount { get; private set; }

        public void Reset() => WalCheckpointCount = 0;

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            if (command.CommandText.Contains("wal_checkpoint", StringComparison.OrdinalIgnoreCase))
            {
                WalCheckpointCount++;
            }
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("wal_checkpoint", StringComparison.OrdinalIgnoreCase))
            {
                WalCheckpointCount++;
            }
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly IInterceptor _interceptor;
        private readonly SqliteConnection _keepAliveConnection;

        public TestDbContextFactory(IInterceptor interceptor)
        {
            _interceptor = interceptor;
            _keepAliveConnection = new SqliteConnection("Data Source=:memory:");
            _keepAliveConnection.Open();
        }

        public AppDbContext CreateDbContext()
        {
            var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_keepAliveConnection)
                .AddInterceptors(_interceptor)
                .Options);
            return ctx;
        }

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateDbContext());
        }
    }
}
