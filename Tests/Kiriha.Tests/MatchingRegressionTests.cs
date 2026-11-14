using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Mpv.UI.Services.Player;
using Kiriha.Mpv.UI.ViewModels.Player;
using Kiriha.Services.Data.Core;
using Kiriha.Services.Data.Mapping;
using Kiriha.Utils.Parsing;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed class MatchingRegressionTests
{
    private static string CreateTempMappingPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "title_mappings.json");
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void AnimeReleaseParser_ExtractsParenthesizedYear_WhileKeepingCleanTitle()
    {
        const string rayearth2026 = "[Erai-raws] Magic Knight Rayearth (2026) - 06 [1080p CR WEB-DL AVC AAC][MultiSub][3329235B]";
        var parsed = AnimeReleaseParser.Parse(rayearth2026);

        Assert.Equal("Magic Knight Rayearth", parsed.Title);
        Assert.Equal(2026, parsed.Year);
        Assert.Equal("06", parsed.Episode);
        Assert.Equal("Erai-raws", parsed.ReleaseGroup);
        Assert.Equal("1080p", parsed.Resolution);

        var parsedRange = AnimeReleaseParser.Parse("[Group] Some Title (2023-2024) [1080p].mkv");
        Assert.Equal("Some Title", parsedRange.Title);
        Assert.Equal(2023, parsedRange.Year);
        Assert.False(parsedRange.IsBatch);
    }

    [Fact]
    public async Task GetIdFromTitleAsync_Remake2026_DoesNotMatch1994OriginalInUserList()
    {
        var path = CreateTempMappingPath();
        try
        {
            var manualMapping = new ManualMappingService(path);
            var recognitionCache = new RecognitionCache();
            var service = new MappingService(null!, manualMapping, null!, recognitionCache);

            const string rayearth2026 = "[Erai-raws] Magic Knight Rayearth (2026) - 06 [1080p CR WEB-DL AVC AAC][MultiSub][3329235B]";

            var only1994 = new List<AnimeEntity>
            {
                new()
                {
                    Id = 435,
                    Title = "Magic Knight Rayearth",
                    RussianTitle = "Рыцари магии",
                    StartYear = 1994,
                    TotalEpisodes = 20,
                    Status = UserAnimeStatus.OnHold
                }
            };
            recognitionCache.BuildIndex(only1994);

            var idWhenOnly1994 = await service.GetIdFromTitleAsync(rayearth2026, only1994);
            Assert.Null(idWhenOnly1994);

            // Even if 1994 entry is un-enriched (StartYear == null), bare fallback must not claim a (2026) remake
            var unenriched1994 = new List<AnimeEntity>
            {
                new()
                {
                    Id = 435,
                    Title = "Magic Knight Rayearth",
                    RussianTitle = "Рыцари магии",
                    StartYear = null,
                    TotalEpisodes = 20,
                    Status = UserAnimeStatus.OnHold
                }
            };
            var idWhenUnenriched = await service.GetIdFromTitleAsync(rayearth2026, unenriched1994);
            Assert.Null(idWhenUnenriched);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task GetIdFromTitleAsync_Remake2026_Matches2026EntryOver1994Original()
    {
        var path = CreateTempMappingPath();
        try
        {
            var manualMapping = new ManualMappingService(path);
            var recognitionCache = new RecognitionCache();
            var service = new MappingService(null!, manualMapping, null!, recognitionCache);

            const string rayearth2026 = "[Erai-raws] Magic Knight Rayearth (2026) - 06 [1080p CR WEB-DL AVC AAC][MultiSub][3329235B]";

            // Case 1: Remake title includes "(2026)"
            var bothWithYearInTitle = new List<AnimeEntity>
            {
                new() { Id = 435, Title = "Magic Knight Rayearth", RussianTitle = "Рыцари магии", StartYear = 1994, Status = UserAnimeStatus.OnHold },
                new() { Id = 60123, Title = "Magic Knight Rayearth (2026)", RussianTitle = "Рыцари магии (2026)", StartYear = 2026, Status = UserAnimeStatus.Watching }
            };
            var id1 = await service.GetIdFromTitleAsync(rayearth2026, bothWithYearInTitle);
            Assert.Equal(60123, id1);

            // Case 2: Remake title is bare (e.g. "Dororo") and disambiguated solely via StartYear
            service.ClearRecognitionCaches();
            var dororoList = new List<AnimeEntity>
            {
                new() { Id = 5760, Title = "Dororo", StartYear = 1969, TotalEpisodes = 26, Status = UserAnimeStatus.Completed },
                new() { Id = 37520, Title = "Dororo", StartYear = 2019, TotalEpisodes = 24, Status = UserAnimeStatus.Watching }
            };
            var dororoId = await service.GetIdFromTitleAsync("[SubsPlease] Dororo (2019) - 06 [1080p].mkv", dororoList);
            Assert.Equal(37520, dororoId);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task SearchOnMalAsync_ReachesLiveMalApiAndSelects2026RemakeOver1994Original()
    {
        var path = CreateTempMappingPath();
        try
        {
            var mockMal = new Mock<IMalApiService>();
            var mockCache = new Mock<IMalSearchCacheRepository>();
            mockCache.Setup(c => c.GetAsync(It.IsAny<string>())).ReturnsAsync((MalSearchCache?)null);

            var rayearth1994 = new AnimeEntity
            {
                Id = 435,
                MediaKind = MediaKind.Anime,
                Title = "Magic Knight Rayearth",
                EnglishTitle = "Magic Knight Rayearth",
                StartYear = 1994,
                TotalEpisodes = 20
            };
            var rayearth2026 = new AnimeEntity
            {
                Id = 60123,
                MediaKind = MediaKind.Anime,
                Title = "Magic Knight Rayearth",
                EnglishTitle = "Magic Knight Rayearth (2026)",
                StartYear = 2026,
                TotalEpisodes = 12
            };

            // Simulate MAL returning 1994 first (higher popularity) and 2026 second
            mockMal.Setup(m => m.SearchAnimeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([rayearth1994, rayearth2026]);

            var recognitionCache = new RecognitionCache();
            recognitionCache.BuildIndex([rayearth1994]);

            var service = new MappingService(mockMal.Object, new ManualMappingService(path), mockCache.Object, recognitionCache);

            const string release = "[Erai-raws] Magic Knight Rayearth (2026) - 06 [1080p CR WEB-DL AVC AAC][MultiSub][3329235B]";

            // Local list only has 1994 -> must return null locally and resolve 60123 via MAL API
            var localId = await service.GetIdFromTitleAsync(release, [rayearth1994]);
            Assert.Null(localId);

            var malId = await service.SearchOnMalAsync(release);
            Assert.Equal(60123, malId);
            mockMal.Verify(m => m.SearchAnimeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task SearchOnMalAsync_RejectsWrongYearAndLowConfidenceCandidates()
    {
        var path = CreateTempMappingPath();
        try
        {
            var mockMal = new Mock<IMalApiService>();
            var mockCache = new Mock<IMalSearchCacheRepository>();
            mockCache.Setup(c => c.GetAsync(It.IsAny<string>())).ReturnsAsync((MalSearchCache?)null);

            // Only 1994 exists in mock response -> must NOT falsely match 1994 when query specifies (2026)
            mockMal.Setup(m => m.SearchAnimeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new AnimeEntity { Id = 435, MediaKind = MediaKind.Anime, Title = "Magic Knight Rayearth", StartYear = 1994, TotalEpisodes = 20 }
                ]);

            var service = new MappingService(mockMal.Object, new ManualMappingService(path), mockCache.Object, new RecognitionCache());
            var wrongYearResult = await service.SearchOnMalAsync("[Erai-raws] Magic Knight Rayearth (2026) - 06 [1080p]");
            Assert.Null(wrongYearResult);

            // Low-confidence unrelated candidate ("Dota Dragons Blood" -> "Kuutei Dragons") must still be rejected
            mockMal.Setup(m => m.SearchAnimeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new AnimeEntity { Id = 39562, MediaKind = MediaKind.Anime, Title = "Kuutei Dragons", StartYear = 2020, TotalEpisodes = 12 }
                ]);

            var lowConfResult = await service.SearchOnMalAsync("Dota Dragons Blood - 01 [1080p].mkv");
            Assert.Null(lowConfResult);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task CompositeKey_MangaAndLightNovelEntries_NeverPolluteAnimeMatching()
    {
        var path = CreateTempMappingPath();
        try
        {
            var recognitionCache = new RecognitionCache();
            var mixedCollection = new List<AnimeEntity>
            {
                new() { Id = 119, MediaKind = MediaKind.Manga, Title = "Magic Knight Rayearth", RussianTitle = "Рыцари магии", StartYear = 2026 },
                new() { Id = 435, MediaKind = MediaKind.LightNovel, Title = "Sousou no Frieren", StartYear = 2023 }
            };
            recognitionCache.BuildIndex(mixedCollection);

            var service = new MappingService(null!, new ManualMappingService(path), null!, recognitionCache);

            Assert.Null(await service.GetIdFromTitleAsync("[Erai-raws] Magic Knight Rayearth (2026) - 06.mkv", mixedCollection));
            Assert.Null(await service.GetIdFromTitleAsync("[SubsPlease] Sousou no Frieren - 01.mkv", mixedCollection));

            // Pipeline must also ignore Manga sharing the same numeric ID as the resolved MAL Anime ID
            var mockTracker = new Mock<ITrackerService>();
            mockTracker.SetupGet(t => t.IsEnabled).Returns(true);
            mockTracker.SetupGet(t => t.TrackerId).Returns("mal");
            mockTracker.Setup(t => t.GetAnimeDetailsAsync(119))
                .ReturnsAsync(new AnimeEntity { Id = 119, MediaKind = MediaKind.Anime, Title = "Real Anime 119", TotalEpisodes = 12 });

            var mockMapping = new Mock<IMappingService>();
            mockMapping.Setup(m => m.GetIdFromTitleAsync(It.IsAny<string>(), It.IsAny<IEnumerable<AnimeEntity>>()))
                .ReturnsAsync(119);

            var pipeline = new MediaMatchingPipeline(mockMapping.Object, [mockTracker.Object]);
            var result = await pipeline.RunAsync(
                new ParsedMedia { OriginalTitle = "Real Anime 119 - 01.mkv", AnimeTitle = "Real Anime 119", Episode = "01" },
                mixedCollection);

            if (result is MediaMatchSuccess success)
            {
                Assert.NotNull(success.MatchedAnime);
                Assert.Equal(MediaKind.Anime, success.MatchedAnime!.MediaKind);
                Assert.Equal("Real Anime 119", success.MatchedAnime.Title);
            }
            else
            {
                Assert.Fail($"Expected MediaMatchSuccess, got {result}");
            }
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task SessionCache_ManualMapping_And_ClearRecognitionCaches_PreserveVariantIsolationAndSynonyms()
    {
        var path = CreateTempMappingPath();
        try
        {
            var recognitionCache = new RecognitionCache();
            var userList = new List<AnimeEntity>
            {
                new()
                {
                    Id = 435,
                    Title = "Magic Knight Rayearth",
                    AlternativeTitles = ["MKRClassic"],
                    StartYear = 1994,
                    Status = UserAnimeStatus.OnHold
                }
            };
            recognitionCache.BuildIndex(userList);

            var service = new MappingService(null!, new ManualMappingService(path), null!, recognitionCache);

            // 1. Play 1994 first (populates _sessionCache for bare title)
            var id1994 = await service.GetIdFromTitleAsync("[Group] Magic Knight Rayearth - 01 [1080p].mkv", userList);
            Assert.Equal(435, id1994);

            // 2. Playing (2026) afterwards must NOT hit 1994's session cache
            var id2026 = await service.GetIdFromTitleAsync("[Erai-raws] Magic Knight Rayearth (2026) - 06 [1080p]", userList);
            Assert.Null(id2026);

            // 3. Unlinking (negative mapping) the (2026) release must NOT negatively map 1994
            service.AddNegativeMapping("[Erai-raws] Magic Knight Rayearth (2026) - 06 [1080p]");
            Assert.True(service.IsNegativelyMapped("[Erai-raws] Magic Knight Rayearth (2026) - 07 [1080p]"));
            Assert.False(service.IsNegativelyMapped("[Group] Magic Knight Rayearth - 02 [1080p].mkv"));

            // 4. ClearRecognitionCaches (triggered by AddNegativeMapping) must NOT wipe user-list synonym matching
            var synonymId = await service.GetIdFromTitleAsync("[Group] MKRClassic - 03 [1080p].mkv", userList);
            Assert.Equal(435, synonymId);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public void PlayerMediaMetadataResolver_And_PlayerViewModel_DoNotMatch2026RemakeTo1994OrMangaInDb()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"Kiriha_Test_Player_Remake_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var factory = new TestDbContextFactory(options);

        try
        {
            using (var db = new AppDbContext(options))
            {
                db.Database.EnsureCreated();
                db.UserAnime.Add(new AnimeEntity
                {
                    Id = 435,
                    MediaKind = MediaKind.Anime,
                    Title = "Magic Knight Rayearth",
                    RussianTitle = "Рыцари магии",
                    StartYear = 1994,
                    TotalEpisodes = 20,
                    Status = UserAnimeStatus.OnHold
                });
                db.UserAnime.Add(new AnimeEntity
                {
                    Id = 119,
                    MediaKind = MediaKind.Manga,
                    Title = "Magic Knight Rayearth (2026)",
                    RussianTitle = "Рыцари магии (2026)",
                    StartYear = 2026,
                    Status = UserAnimeStatus.Watching
                });
                db.SaveChanges();
            }

            var resolver = new PlayerMediaMetadataResolver(factory);
            const string releaseName = "[Erai-raws] Magic Knight Rayearth (2026) - 06 [1080p CR WEB-DL AVC AAC][MultiSub][3329235B]";

            // 1. Direct file resolution must NOT resolve to 435 (1994) or 119 (Manga)
            var resolved = resolver.Resolve($@"C:\Anime\{releaseName}.mkv");
            Assert.Null(resolved.AnimeId);
            Assert.Equal("Magic Knight Rayearth (2026)", resolved.TitleRomaji);

            // 2. PlayerViewModel constructed with TorrServer stream + stripped TitleRu (exact Torrents LaunchStream scenario)
            //    must inspect OriginalTitle and NOT resolve to 435 via ResolveByTitle
            var settings = new AppSettings();
            var mockSettings = new Mock<ISettingsService>();
            mockSettings.SetupGet(s => s.Current).Returns(settings);
            var mockLocalizer = new Mock<ILocalizer>();

            var streamMetadata = new PlayerMediaMetadata(
                OriginalTitle: releaseName,
                TitleRu: "Magic Knight Rayearth",
                TitleEn: releaseName,
                EpisodeText: "06",
                AnimeId: null,
                TitleRomaji: releaseName,
                TorrentHash: "abcd1234");

            using var vm = new PlayerViewModel(
                "http://127.0.0.1:8090/stream/fname.mkv?link=abcd1234&index=1&play",
                streamMetadata,
                resolver,
                mockSettings.Object,
                mockLocalizer.Object);

            Assert.Null(vm.AnimeId);
        }
        finally
        {
            try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public void RssFeedService_MatchActiveAnime_DistinguishesRemakeYearFromOriginal()
    {
        var rayearth1994 = new AnimeEntity
        {
            Id = 435,
            MediaKind = MediaKind.Anime,
            Title = "Magic Knight Rayearth",
            StartYear = 1994,
            Status = UserAnimeStatus.Watching
        };
        var rayearth2026 = new AnimeEntity
        {
            Id = 60123,
            MediaKind = MediaKind.Anime,
            Title = "Magic Knight Rayearth (2026)",
            StartYear = 2026,
            Status = UserAnimeStatus.Watching
        };
        var mangaSameName = new AnimeEntity
        {
            Id = 119,
            MediaKind = MediaKind.Manga,
            Title = "Magic Knight Rayearth (2026)",
            StartYear = 2026,
            Status = UserAnimeStatus.Watching
        };

        // Only 1994 + Manga in active list -> 2026 torrent must NOT match 1994 or Manga
        Assert.Null(RssFeedService.MatchActiveAnime([rayearth1994, mangaSameName], "Magic Knight Rayearth", parsedSeason: null, parsedYear: 2026));

        // Both 1994 and 2026 in active list -> 2026 torrent matches 2026 entry
        var matched = RssFeedService.MatchActiveAnime([rayearth1994, rayearth2026], "Magic Knight Rayearth", parsedSeason: null, parsedYear: 2026);
        Assert.Same(rayearth2026, matched);
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
