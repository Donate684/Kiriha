using System.Reflection;
using System.Text.Json;
using Kiriha.Core;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking;
using Kiriha.Core.Tracking.Mapping;
using Kiriha.Data.Core;
using Kiriha.Data.Repository;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using IAppHistoryRepository = Kiriha.Core.Abstractions.Repositories.IHistoryRepository;

namespace Kiriha.Tests;

public sealed class CoreRefactoringWave3Tests
{
    [Fact]
    public void HistoryContext_IsDeleted_And_CoreInterfacesHaveZeroDefaultInterfaceMethods()
    {
        var domainAssembly = typeof(AnimeEntity).Assembly;
        Assert.Null(domainAssembly.GetType("Kiriha.Core.Domain.Models.HistoryContext"));

        Type[] interfaces = [typeof(IHistoryService), typeof(IAppHistoryRepository), typeof(ISyncManager)];
        foreach (var iface in interfaces)
        {
            foreach (var method in iface.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Assert.True(
                    method.IsAbstract,
                    $"Interface '{iface.Name}' still defines a Default Interface Method: '{method.Name}'.");
            }
        }

        // Verify string actionType overloads are completely gone from IHistoryService
        var addEntryMethods = typeof(IHistoryService)
            .GetMethods()
            .Where(m => m.Name is nameof(IHistoryService.AddEntryAsync) or nameof(IHistoryService.AddEntry))
            .ToList();

        Assert.Equal(2, addEntryMethods.Count);
        foreach (var method in addEntryMethods)
        {
            Assert.DoesNotContain(
                method.GetParameters(),
                p => p.Name == "actionType" && p.ParameterType == typeof(string));
        }
    }

    [Fact]
    public void LegacyLocalizationFacades_AreDeleted_And_PassthroughLocalizerFormatsCleanly()
    {
        var trackingAssembly = typeof(TrackingServicesRegistration).Assembly;
        var uiAssembly = typeof(Kiriha.App).Assembly;

        Assert.Null(trackingAssembly.GetType("Kiriha.Core.Tracking.Utils.TrackingLoc"));
        Assert.Null(uiAssembly.GetType("Kiriha.Core.Localization.LocalizationStore"));
        Assert.Null(typeof(UIUtils).GetMethod("GetLoc", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        var localizer = ILocalizer.Passthrough;
        Assert.Equal("sync.status.ok", localizer.GetLoc("sync.status.ok"));
        Assert.Equal("Episode 12", localizer.GetLoc("Episode {0}", 12));
    }

    [Fact]
    public async Task ShikiMetadata_UsesCompositeKeyWithoutBitmasks()
    {
        // Bitmask constants and helper must no longer exist
        Assert.Null(typeof(ShikiMetadata).GetField("MangaIdMask", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        Assert.Null(typeof(ShikiMetadata).GetField("LightNovelIdMask", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        Assert.Null(typeof(ShikiMetadata).GetMethod("GetCacheId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var factory = await CreateInitializedFactoryAsync(conn);
        var repo = new MetadataRepository(factory);

        await repo.UpsertAsync(new ShikiMetadata { Id = 42, MediaKind = MediaKind.Anime, Russian = "Аниме 42", FetchedAt = DateTime.UtcNow });
        await repo.UpsertAsync(new ShikiMetadata { Id = 42, MediaKind = MediaKind.Manga, Russian = "Манга 42", FetchedAt = DateTime.UtcNow });
        await repo.UpsertAsync(new ShikiMetadata { Id = 42, MediaKind = MediaKind.LightNovel, Russian = "Ранобэ 42", FetchedAt = DateTime.UtcNow });

        var animeMeta = await repo.GetAsync(42, MediaKind.Anime);
        var mangaMeta = await repo.GetAsync(42, MediaKind.Manga);
        var novelMeta = await repo.GetAsync(42, MediaKind.LightNovel);

        Assert.Equal("Аниме 42", animeMeta?.Russian);
        Assert.Equal("Манга 42", mangaMeta?.Russian);
        Assert.Equal("Ранобэ 42", novelMeta?.Russian);

        var batch = await repo.GetBatchAsync(
        [
            new MediaKey(42, MediaKind.Anime),
            new MediaKey(42, MediaKind.Manga),
            new MediaKey(42, MediaKind.LightNovel),
        ]);

        Assert.Equal(3, batch.Count);
        Assert.Equal("Аниме 42", batch[new MediaKey(42, MediaKind.Anime)].Russian);
        Assert.Equal("Манга 42", batch[new MediaKey(42, MediaKind.Manga)].Russian);
        Assert.Equal("Ранобэ 42", batch[new MediaKey(42, MediaKind.LightNovel)].Russian);
    }

    [Fact]
    public async Task Wave3StorageUnificationMigration_UnmasksBitmaskedMetadataRowsFromV18()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;

        await using (var preContext = new AppDbContext(options))
        {
            var migrator = preContext.GetService<IMigrator>();
            // Migrate up to just before Wave3StorageUnification
            await migrator.MigrateAsync("20261116105000_CoreSchemaCleanup");

            // Insert legacy bitmasked metadata rows (0x40000000 = Manga, 0x20000000 = LightNovel)
            const int mangaMaskedId = 25 | 0x40000000;
            const int novelMaskedId = 35 | 0x20000000;
            await preContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "metadata" ("id", "name", "russian", "fetched_at")
                VALUES
                    (15, 'Anime15', 'RuAnime15', '2026-11-15T00:00:00Z'),
                    ({0}, 'Manga25', 'RuManga25', '2026-11-15T00:00:00Z'),
                    ({1}, 'Novel35', 'RuNovel35', '2026-11-15T00:00:00Z');
                """,
                mangaMaskedId,
                novelMaskedId);

            // Apply Wave3StorageUnification
            await migrator.MigrateAsync();
        }

        var factory = new TestDbFactory(options);
        var repo = new MetadataRepository(factory);

        var anime = await repo.GetAsync(15, MediaKind.Anime);
        var manga = await repo.GetAsync(25, MediaKind.Manga);
        var novel = await repo.GetAsync(35, MediaKind.LightNovel);

        Assert.NotNull(anime);
        Assert.Equal("RuAnime15", anime!.Russian);

        Assert.NotNull(manga);
        Assert.Equal("RuManga25", manga!.Russian);

        Assert.NotNull(novel);
        Assert.Equal("RuNovel35", novel!.Russian);
    }

    [Fact]
    public async Task LegacyJsonStores_AutoImportIntoSqliteAndDeleteLegacyFiles()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var factory = await CreateInitializedFactoryAsync(conn);

        var tempDir = Path.Combine(Path.GetTempPath(), $"KirihaWave3_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var mappingsJson = Path.Combine(tempDir, "title_mappings.json");
        var scrobbleJson = Path.Combine(tempDir, "scrobble_progress.json");
        var resumeJson = Path.Combine(tempDir, "playback_resume.json");

        try
        {
            await File.WriteAllTextAsync(mappingsJson, """{"version":2,"mappings":{"frieren":52991,"ignored release":-1}}""");
            await File.WriteAllTextAsync(scrobbleJson, """{"52991_3":245}""");
            await File.WriteAllTextAsync(resumeJson, JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["https://stream.test/ep3.m3u8"] = new
                {
                    Position = 612.5,
                    Duration = 1420.0,
                    UpdatedAt = DateTime.UtcNow
                }
            }));

            // 1. ManualMappingService imports title_mappings.json and deletes it
            var titleRepo = new TitleMappingRepository(factory);
            var mappingService = new ManualMappingService(titleRepo, mappingsJson);
            Assert.True(mappingService.TryGetMapping("Frieren", out var mappedId));
            Assert.Equal(52991, mappedId);
            Assert.True(mappingService.IsNegativelyMapped("Ignored Release"));
            await mappingService.FlushAsync();
            Assert.False(File.Exists(mappingsJson));

            // Reload from SQLite alone (no JSON file)
            var reloadedMapping = new ManualMappingService(titleRepo);
            Assert.True(reloadedMapping.TryGetMapping("frieren", out var persistedId));
            Assert.Equal(52991, persistedId);

            // 2. SqliteScrobbleProgressStore imports scrobble_progress.json and deletes it
            var scrobbleStore = new SqliteScrobbleProgressStore(factory, scrobbleJson);
            Assert.Equal(245, scrobbleStore.GetElapsed("52991_3"));
            await scrobbleStore.FlushAsync();
            Assert.False(File.Exists(scrobbleJson));

            var reloadedScrobble = new SqliteScrobbleProgressStore(factory);
            Assert.Equal(245, reloadedScrobble.GetElapsed("52991_3"));

            // 3. SqlitePlaybackResumeStore imports playback_resume.json and deletes it
            var resumeStore = new SqlitePlaybackResumeStore(factory, resumeJson);
            var pos = resumeStore.TryGetResumePosition("https://stream.test/ep3.m3u8");
            Assert.NotNull(pos);
            Assert.Equal(612.5, pos!.Value, 1);
            await resumeStore.FlushAsync();
            Assert.False(File.Exists(resumeJson));

            var reloadedResume = new SqlitePlaybackResumeStore(factory);
            Assert.Equal(612.5, reloadedResume.TryGetResumePosition("https://stream.test/ep3.m3u8")!.Value, 1);

            // 4. AniListMediaMapRepository stores ANIME and MANGA mappings independently
            var aniListMapRepo = new AniListMediaMapRepository(factory);
            await aniListMapRepo.UpsertAsync(77, "ANIME", 10077);
            await aniListMapRepo.UpsertAsync(77, "MANGA", 20077);

            Assert.Equal(10077, await aniListMapRepo.GetAniListIdAsync(77, "ANIME"));
            Assert.Equal(20077, await aniListMapRepo.GetAniListIdAsync(77, "MANGA"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    private static async Task<IDbContextFactory<AppDbContext>> CreateInitializedFactoryAsync(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        return new TestDbFactory(options);
    }

    private sealed class TestDbFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
