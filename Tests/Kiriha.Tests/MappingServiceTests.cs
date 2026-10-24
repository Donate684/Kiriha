using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Services.Data.Mapping;

namespace Kiriha.Tests;

public sealed class MappingServiceTests
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
    public async Task GetIdFromTitleAsync_MatchesS2Correctly()
    {
        var path = CreateTempMappingPath();
        try
        {
            var manualMapping = new ManualMappingService(path);
            var service = new MappingService(null!, manualMapping, null!, new RecognitionCache());

            var userList = new List<AnimeEntity>
            {
                new AnimeEntity { Id = 1, Title = "Sousou no Frieren" },
                new AnimeEntity { Id = 2, Title = "Sousou no Frieren Season 2" }
            };

            var id = await service.GetIdFromTitleAsync("[Erai-raws] Sousou no Frieren 2nd Season - 01 [1080p].mkv", userList);
            Assert.Equal(2, id);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task GetIdFromTitleAsync_MatchesOvaCorrectly()
    {
        var path = CreateTempMappingPath();
        try
        {
            var manualMapping = new ManualMappingService(path);
            var service = new MappingService(null!, manualMapping, null!, new RecognitionCache());

            var userList = new List<AnimeEntity>
            {
                new AnimeEntity { Id = 1, Title = "KonoSuba" },
                new AnimeEntity { Id = 2, Title = "KonoSuba OVA" }
            };

            var id = await service.GetIdFromTitleAsync("KonoSuba OVA 1.mkv", userList);
            Assert.Equal(2, id);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task GetIdFromTitleAsync_MatchesOnaCorrectly()
    {
        var path = CreateTempMappingPath();
        try
        {
            var manualMapping = new ManualMappingService(path);
            var service = new MappingService(null!, manualMapping, null!, new RecognitionCache());

            var userList = new List<AnimeEntity>
            {
                new AnimeEntity { Id = 1, Title = "Cyberpunk Edgerunners" },
                new AnimeEntity { Id = 2, Title = "Cyberpunk Edgerunners ONA" }
            };

            var id = await service.GetIdFromTitleAsync("Cyberpunk Edgerunners ONA 01.mkv", userList);
            Assert.Equal(2, id);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task GetIdFromTitleAsync_MatchesCyrillicCorrectly()
    {
        var path = CreateTempMappingPath();
        try
        {
            var manualMapping = new ManualMappingService(path);
            var service = new MappingService(null!, manualMapping, null!, new RecognitionCache());

            var userList = new List<AnimeEntity>
            {
                new AnimeEntity { Id = 1, Title = "Sousou no Frieren", RussianTitle = "Фрирен, провожающая в последний путь" }
            };

            var id = await service.GetIdFromTitleAsync("[Субтитры] Фрирен, провожающая в последний путь - 01.mkv", userList);
            Assert.Equal(1, id);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task GetIdFromTitleAsync_DoesNotFallbackToS1IfS2ExplicitlyRequested()
    {
        var path = CreateTempMappingPath();
        try
        {
            var manualMapping = new ManualMappingService(path);
            var service = new MappingService(null!, manualMapping, null!, new RecognitionCache());

            var userList = new List<AnimeEntity>
            {
                new AnimeEntity { Id = 1, Title = "Sousou no Frieren" }
            };

            var id = await service.GetIdFromTitleAsync("[Erai-raws] Sousou no Frieren Season 2 - 01.mkv", userList);
            Assert.Null(id);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task RemoveMapping_RemovesByBothOriginalFilenameAndCleanTitle()
    {
        var path = CreateTempMappingPath();
        try
        {
            var manualMapping = new ManualMappingService(path);
            var service = new MappingService(null!, manualMapping, null!, new RecognitionCache());

            const string dirtyFilename = "[Erai-raws] Tensei Shitara Ken Deshita II - 04 [1080p][D47E347C].mkv";
            const string cleanTitle = "Tensei Shitara Ken Deshita II";

            // 1. Bind with dirty filename -> should be recognized by both dirty filename and clean title
            service.AddMapping(dirtyFilename, 53913);
            Assert.True(service.IsManuallyMapped(dirtyFilename));
            Assert.True(service.IsManuallyMapped(cleanTitle));

            var id = await service.GetIdFromTitleAsync(dirtyFilename, []);
            Assert.Equal(53913, id);

            // Remove using dirty filename -> both must be unmapped
            service.RemoveMapping(dirtyFilename);
            Assert.False(service.IsManuallyMapped(dirtyFilename));
            Assert.False(service.IsManuallyMapped(cleanTitle));
            id = await service.GetIdFromTitleAsync(dirtyFilename, []);
            Assert.Null(id);

            // 2. Bind with clean title -> should be recognized by both
            service.AddMapping(cleanTitle, 53913);
            Assert.True(service.IsManuallyMapped(dirtyFilename));
            Assert.True(service.IsManuallyMapped(cleanTitle));

            // Remove using dirty filename -> both must be unmapped
            service.RemoveMapping(dirtyFilename);
            Assert.False(service.IsManuallyMapped(dirtyFilename));
            Assert.False(service.IsManuallyMapped(cleanTitle));
            id = await service.GetIdFromTitleAsync(dirtyFilename, []);
            Assert.Null(id);

            // 3. Bind with dirty filename and remove using clean title -> both must be unmapped
            service.AddMapping(dirtyFilename, 53913);
            Assert.True(service.IsManuallyMapped(dirtyFilename));
            Assert.True(service.IsManuallyMapped(cleanTitle));

            service.RemoveMapping(cleanTitle);
            Assert.False(service.IsManuallyMapped(dirtyFilename));
            Assert.False(service.IsManuallyMapped(cleanTitle));
            id = await service.GetIdFromTitleAsync(dirtyFilename, []);
            Assert.Null(id);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task GetIdFromTitleAsync_RomanNumeralSequel_DoesNotFallbackToSeason1()
    {
        var path = CreateTempMappingPath();
        try
        {
            var manualMapping = new ManualMappingService(path);
            var service = new MappingService(null!, manualMapping, null!, new RecognitionCache());

            var userList = new List<AnimeEntity>
            {
                new AnimeEntity { Id = 49891, Title = "Tensei shitara Ken deshita" }
            };

            var id = await service.GetIdFromTitleAsync("[Erai-raws] Tensei Shitara Ken Deshita II - 04 [1080p][D47E347C].mkv", userList);
            Assert.Null(id);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task GetIdFromTitleAsync_RomanNumeralSequel_MatchesSeason2InUserList()
    {
        var path = CreateTempMappingPath();
        try
        {
            var manualMapping = new ManualMappingService(path);
            var service = new MappingService(null!, manualMapping, null!, new RecognitionCache());

            var userList = new List<AnimeEntity>
            {
                new AnimeEntity { Id = 49891, Title = "Tensei shitara Ken deshita" },
                new AnimeEntity { Id = 53913, Title = "Tensei shitara Ken deshita 2nd Season" }
            };

            var id = await service.GetIdFromTitleAsync("[Erai-raws] Tensei Shitara Ken Deshita II - 04 [1080p][D47E347C].mkv", userList);
            Assert.Equal(53913, id);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public void ManualMappingService_LegacyFlatJson_IsResetOnLoad()
    {
        var path = CreateTempMappingPath();
        try
        {
            File.WriteAllText(path, """{"tensei shitara ken deshita ii": 49891}""");

            var service = new ManualMappingService(path);

            Assert.False(service.TryGetMapping("tensei shitara ken deshita ii", out _));
            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public void ManualMappingService_OutdatedVersionJson_IsResetOnLoad()
    {
        var path = CreateTempMappingPath();
        try
        {
            File.WriteAllText(path, """{"version": 1, "mappings": {"sousou no frieren": 123}}""");

            var service = new ManualMappingService(path);

            Assert.False(service.TryGetMapping("sousou no frieren", out _));
            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task ManualMappingService_CurrentVersionJson_PersistsAndReloadsCorrectly()
    {
        var path = CreateTempMappingPath();
        try
        {
            var service = new ManualMappingService(path);
            service.AddMapping("Sousou no Frieren", 52991);
            await service.FlushAsync();

            var reloaded = new ManualMappingService(path);

            Assert.True(reloaded.TryGetMapping("sousou no frieren", out int id));
            Assert.Equal(52991, id);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public void ManualMappingService_CorruptJson_IsResetOnLoad()
    {
        var path = CreateTempMappingPath();
        try
        {
            File.WriteAllText(path, "{ corrupt json !!!");

            var service = new ManualMappingService(path);

            Assert.False(service.TryGetMapping("test", out _));
            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteQuietly(path);
        }
    }
}
