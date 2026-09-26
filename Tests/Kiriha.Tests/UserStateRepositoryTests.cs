using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core.Domain.Models;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Repository;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kiriha.Tests;

public sealed class UserStateRepositoryTests
{
    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;
        public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;
        public AppDbContext CreateDbContext() => new(_options);
        public Task<AppDbContext> CreateDbContextAsync(System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(new AppDbContext(_options));
    }

    private static (AppDbContext context, IDbContextFactory<AppDbContext> factory, string dbPath) CreateTestDb()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", Guid.NewGuid() + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var factory = new TestDbContextFactory(options);
        var context = new AppDbContext(options);
        context.Database.Migrate();

        return (context, factory, dbPath);
    }

    private static void CleanupTestDb(string dbPath)
    {
        try { File.Delete(dbPath); } catch { }
        try { File.Delete(dbPath + "-wal"); } catch { }
        try { File.Delete(dbPath + "-shm"); } catch { }
    }

    [Fact]
    public async Task SeasonalHiddenRepository_AddRemoveAndQuery_WorksCorrectly()
    {
        var (context, factory, dbPath) = CreateTestDb();
        try
        {
            var repo = new SeasonalHiddenRepository(factory);
            await repo.InitializeAsync();

            Assert.False(repo.IsHidden(101));
            Assert.Empty(repo.GetHiddenIds());

            await repo.AddAsync(101);
            await repo.AddAsync(102);

            Assert.True(repo.IsHidden(101));
            Assert.True(repo.IsHidden(102));
            Assert.Contains(101, repo.GetHiddenIds());
            Assert.Contains(102, repo.GetHiddenIds());

            // Check new instance loads from DB
            var repo2 = new SeasonalHiddenRepository(factory);
            await repo2.InitializeAsync();
            Assert.True(repo2.IsHidden(101));
            Assert.True(repo2.IsHidden(102));

            await repo.RemoveAsync(101);
            Assert.False(repo.IsHidden(101));
            Assert.True(repo.IsHidden(102));

            await repo.AddAsync(103);
            await repo.RemoveRangeAsync([102, 103]);
            Assert.False(repo.IsHidden(102));
            Assert.False(repo.IsHidden(103));
        }
        finally
        {
            await context.DisposeAsync();
            CleanupTestDb(dbPath);
        }
    }

    [Fact]
    public async Task TorrentFilterRepository_HiddenAnimeAndPerTitleFilters_PersistCorrectly()
    {
        var (context, factory, dbPath) = CreateTestDb();
        try
        {
            var repo = new TorrentFilterRepository(factory);
            await repo.InitializeAsync();

            Assert.False(repo.IsAnimeHidden(501));

            await repo.SetAnimeHiddenAsync(501, true);
            Assert.True(repo.IsAnimeHidden(501));

            var filter = new AppSettings.TorrentFilterSet
            {
                OnlyCrunchyroll = true,
                Filter1080p = true,
                UseCustomQuery = true,
                CustomQuery = "Frieren Special"
            };

            await repo.SaveFilterAsync(501, filter);

            var cached = repo.TryGetCachedFilter(501);
            Assert.NotNull(cached);
            Assert.True(cached.OnlyCrunchyroll);
            Assert.True(cached.Filter1080p);
            Assert.Equal("Frieren Special", cached.CustomQuery);

            // Check new instance reloads from DB
            var repo2 = new TorrentFilterRepository(factory);
            await repo2.InitializeAsync();
            Assert.True(repo2.IsAnimeHidden(501));

            var loaded = await repo2.GetFilterAsync(501);
            Assert.NotNull(loaded);
            Assert.True(loaded.OnlyCrunchyroll);
            Assert.True(loaded.Filter1080p);
            Assert.Equal("Frieren Special", loaded.CustomQuery);

            await repo2.SetAnimeHiddenAsync(501, false);
            Assert.False(repo2.IsAnimeHidden(501));
        }
        finally
        {
            await context.DisposeAsync();
            CleanupTestDb(dbPath);
        }
    }

    [Fact]
    public async Task LegacyUserStateMigration_MigratesJsonDataToSqlite()
    {
        var (context, factory, dbPath) = CreateTestDb();
        var tempConfigFile = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", Guid.NewGuid() + ".json");
        try
        {
            var json = """
            {
              "UI": {
                "HiddenSeasonalIds": [10, 20, 30]
              },
              "Torrents": {
                "HiddenAnimeIds": [100, 200],
                "PerTitleFilters": {
                  "300": {
                    "OnlyCrunchyroll": true,
                    "FilterNetflix": false,
                    "Filter1080p": true,
                    "UseCustomQuery": true,
                    "CustomQuery": "Sousou no Frieren"
                  }
                }
              }
            }
            """;
            await File.WriteAllTextAsync(tempConfigFile, json);

            await LegacyUserStateMigration.MigrateAsync(context, tempConfigFile);

            var seasonal = await context.HiddenSeasonalAnime.Select(x => x.AnimeId).ToListAsync();
            Assert.Contains(10, seasonal);
            Assert.Contains(20, seasonal);
            Assert.Contains(30, seasonal);

            var torrentHidden = await context.HiddenTorrentAnime.Select(x => x.AnimeId).ToListAsync();
            Assert.Contains(100, torrentHidden);
            Assert.Contains(200, torrentHidden);

            var filter = await context.TorrentTitleFilters.FirstOrDefaultAsync(x => x.AnimeId == 300);
            Assert.NotNull(filter);
            Assert.True(filter.OnlyCrunchyroll);
            Assert.True(filter.Filter1080p);
            Assert.True(filter.UseCustomQuery);
            Assert.Equal("Sousou no Frieren", filter.CustomQuery);
        }
        finally
        {
            try { File.Delete(tempConfigFile); } catch { }
            await context.DisposeAsync();
            CleanupTestDb(dbPath);
        }
    }
}
