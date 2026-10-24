using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Infrastructure.Player;
using Kiriha.Mpv.UI.Services.Player;
using Kiriha.Services.Data.Core;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Tests;

public sealed class PlayerMediaMetadataTests
{
    [Fact]
    public void FromVideoPath_UsesFileNameWithoutExtensionAsFallbackTitle()
    {
        var metadata = PlayerMediaMetadata.FromVideoPath(@"C:\Anime\Cowboy Bebop - 01.mkv");

        Assert.Equal("Cowboy Bebop - 01", metadata.TitleRu);
        Assert.Equal(string.Empty, metadata.TitleEn);
        Assert.Equal(string.Empty, metadata.EpisodeText);
        Assert.Null(metadata.AnimeId);
    }

    [Fact]
    public void FromVideoPath_BlankPathReturnsEmptyMetadata()
    {
        var metadata = PlayerMediaMetadata.FromVideoPath(" ");

        Assert.Equal(string.Empty, metadata.TitleRu);
        Assert.Equal(string.Empty, metadata.TitleEn);
        Assert.Equal(string.Empty, metadata.EpisodeText);
        Assert.Null(metadata.AnimeId);
    }

    [Theory]
    [InlineData(new[] { "--player", PlayerProcessBridge.ResidentArg }, true)]
    [InlineData(new[] { "--PLAYER", "--PLAYER-RESIDENT" }, true)]
    [InlineData(new[] { "--player" }, false)]
    public void PlayerProcessBridge_IsResident_IsCaseInsensitive(string[] args, bool expected)
    {
        Assert.Equal(expected, PlayerProcessBridge.IsResident(args));
    }

    [Fact]
    public void FilenameResolver_ReturnsFallbackForBlankPath()
    {
        var resolver = new FilenamePlayerMediaMetadataResolver();

        var metadata = resolver.Resolve("");

        Assert.Equal(string.Empty, metadata.TitleRu);
        Assert.Equal(string.Empty, metadata.TitleEn);
        Assert.Equal(string.Empty, metadata.EpisodeText);
        Assert.Null(metadata.AnimeId);
    }

    [Theory]
    [InlineData(@"C:\Anime\[SubsPlease] Sousou no Frieren - 12 (1080p).mkv", "Sousou no Frieren", "12")]
    [InlineData(@"C:\Anime\[Erai-raws] Oshi no Ko - S02E03 [1080p].mkv", "Oshi no Ko", "03")]
    [InlineData(@"C:\Anime\Fullmetal Alchemist Brotherhood - 01.mkv", "Fullmetal Alchemist Brotherhood", "01")]
    public void FilenameResolver_ExtractsTitleAndEpisodeFromCommonReleaseNames(
        string path,
        string expectedTitle,
        string expectedEpisode)
    {
        var resolver = new FilenamePlayerMediaMetadataResolver();

        var metadata = resolver.Resolve(path);

        Assert.Equal(expectedTitle, metadata.TitleRu);
        Assert.Equal(expectedTitle, metadata.TitleRomaji);
        Assert.Equal(expectedEpisode, metadata.EpisodeText);
        Assert.Null(metadata.AnimeId);
    }

    [Fact]
    public void FromVideoPath_PopulatesTitleRomaji()
    {
        var metadata = PlayerMediaMetadata.FromVideoPath(@"C:\Anime\Steins Gate - 01.mkv");

        Assert.Equal("Steins Gate - 01", metadata.TitleRomaji);
        Assert.Equal("Steins Gate - 01", metadata.TitleRu);
    }

    [Fact]
    public void PlayerViewModel_HeaderTitles_UseRussianTitlesFalse_RomajiOnTopEnglishOnBottom()
    {
        var settings = new AppSettings();
        settings.UI.UseRussianTitles = false;
        var mockSettings = new Moq.Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);
        var mockLocalizer = new Moq.Mock<ILocalizer>();

        var vm = new Kiriha.Mpv.UI.ViewModels.Player.PlayerViewModel(
            @"C:\Anime\frieren_01.mkv",
            null,
            null,
            mockSettings.Object,
            mockLocalizer.Object);

        vm.ApplyExternalMetadata(new PlayerMediaMetadata(
            "frieren_01",
            "Провожающая в последний путь Фрирен",
            "Frieren: Beyond Journey's End",
            "01",
            52991,
            "Sousou no Frieren"));

        Assert.Equal("Sousou no Frieren", vm.TopTitle);
        Assert.Equal("Frieren: Beyond Journey's End", vm.BottomTitle);
        Assert.True(vm.HasBottomTitle);
    }

    [Fact]
    public void PlayerViewModel_HeaderTitles_UseRussianTitlesTrue_RussianOnTopEnglishOnBottom()
    {
        var settings = new AppSettings();
        settings.UI.UseRussianTitles = true;
        var mockSettings = new Moq.Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);
        var mockLocalizer = new Moq.Mock<ILocalizer>();

        var vm = new Kiriha.Mpv.UI.ViewModels.Player.PlayerViewModel(
            @"C:\Anime\frieren_01.mkv",
            null,
            null,
            mockSettings.Object,
            mockLocalizer.Object);

        vm.ApplyExternalMetadata(new PlayerMediaMetadata(
            "frieren_01",
            "Провожающая в последний путь Фрирен",
            "Frieren: Beyond Journey's End",
            "01",
            52991,
            "Sousou no Frieren"));

        Assert.Equal("Провожающая в последний путь Фрирен", vm.TopTitle);
        Assert.Equal("Sousou no Frieren", vm.BottomTitle);
        Assert.True(vm.HasBottomTitle);
    }

    [Fact]
    public void PlayerViewModel_ApplyExternalMetadata_FiresHeaderPropertyChanges()
    {
        var settings = new AppSettings();
        settings.UI.UseRussianTitles = true;
        var mockSettings = new Moq.Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);
        var mockLocalizer = new Moq.Mock<ILocalizer>();

        var vm = new Kiriha.Mpv.UI.ViewModels.Player.PlayerViewModel(
            @"C:\Anime\frieren_01.mkv",
            null,
            null,
            mockSettings.Object,
            mockLocalizer.Object);

        var changedProperties = new System.Collections.Generic.List<string>();
        vm.PropertyChanged += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
                changedProperties.Add(e.PropertyName);
        };

        vm.ApplyExternalMetadata(new PlayerMediaMetadata(
            "frieren_01",
            "Провожающая в последний путь Фрирен",
            "Frieren: Beyond Journey's End",
            "01",
            52991,
            "Sousou no Frieren"));

        Assert.Contains(nameof(vm.TopTitle), changedProperties);
        Assert.Contains(nameof(vm.BottomTitle), changedProperties);
        Assert.Contains(nameof(vm.HasBottomTitle), changedProperties);
        Assert.Contains(nameof(vm.EpisodeTitle), changedProperties);
        Assert.Contains(nameof(vm.HasEpisodeAndBottom), changedProperties);
    }

    [Fact]
    public void PlayerViewModel_MatchesOriginalTitle_MatchesVariousFormats()
    {
        var settings = new AppSettings();
        var mockSettings = new Moq.Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);
        var mockLocalizer = new Moq.Mock<ILocalizer>();

        var vm = new Kiriha.Mpv.UI.ViewModels.Player.PlayerViewModel(
            @"C:\Anime\[SubsPlease] Sousou no Frieren - 01.mkv",
            null,
            null,
            mockSettings.Object,
            mockLocalizer.Object);

        vm.ApplyExternalMetadata(new PlayerMediaMetadata(
            "[SubsPlease] Sousou no Frieren - 01",
            "Провожающая в последний путь Фрирен",
            "Frieren: Beyond Journey's End",
            "01",
            52991,
            "Sousou no Frieren"));

        Assert.True(vm.MatchesOriginalTitle("[SubsPlease] Sousou no Frieren - 01"));
        Assert.True(vm.MatchesOriginalTitle("Sousou no Frieren"));
        Assert.True(vm.MatchesOriginalTitle("Провожающая в последний путь Фрирен"));
        Assert.True(vm.MatchesOriginalTitle("Frieren: Beyond Journey's End"));
    }

    [Fact]
    public void PlayerViewModel_ApplyExternalMetadata_DoesNotOverwriteRussianTitleWithEmptyString()
    {
        var settings = new AppSettings();
        settings.UI.UseRussianTitles = true;
        var mockSettings = new Moq.Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);
        var mockLocalizer = new Moq.Mock<ILocalizer>();

        var vm = new Kiriha.Mpv.UI.ViewModels.Player.PlayerViewModel(
            @"C:\Anime\Ghost Meets Gal - 01.mkv",
            null,
            null,
            mockSettings.Object,
            mockLocalizer.Object);

        // First apply Russian metadata (e.g. fetched from Shikimori)
        vm.ApplyExternalMetadata(new PlayerMediaMetadata(
            "Ghost Meets Gal - 01",
            "Призрак встречает гяру!",
            "",
            "01",
            58494,
            "Ghost Meets Gal!"));

        Assert.Equal("Призрак встречает гяру!", vm.TopTitle);
        Assert.Equal("Ghost Meets Gal!", vm.BottomTitle);

        // Now simulate incoming metadata with empty titleRu (e.g. bare MAL lookup that lacks Russian title)
        vm.ApplyExternalMetadata(new PlayerMediaMetadata(
            "Ghost Meets Gal - 01",
            "",
            "",
            "01",
            58494,
            "Ghost Meets Gal!"));

        // Russian title MUST NOT be wiped out with empty string!
        Assert.Equal("Призрак встречает гяру!", vm.TopTitle);
        Assert.Equal("Ghost Meets Gal!", vm.BottomTitle);
    }

    [Theory]
    [InlineData("https://cache.libria.fun/videos/media/ts/9876/1/720.mp4:hls:manifest.m3u8")]
    [InlineData("https://stream.example.com/anime/master.m3u8")]
    [InlineData("https://stream.example.com/hls/index.m3u8")]
    [InlineData("https://stream.example.com/playlist.m3u8")]
    public void FilenameResolver_StreamManifestUrl_DoesNotParseAsAnimeTitle(string url)
    {
        var resolver = new FilenamePlayerMediaMetadataResolver();

        var metadata = resolver.Resolve(url);

        Assert.Equal(string.Empty, metadata.TitleRu);
        Assert.Equal(string.Empty, metadata.TitleRomaji);
        Assert.Equal(string.Empty, metadata.OriginalTitle);
        Assert.Null(metadata.AnimeId);
    }

    [Fact]
    public void PlayerMediaMetadata_FromVideoPath_StreamManifest_ReturnsEmptyFallbackTitle()
    {
        var metadata = PlayerMediaMetadata.FromVideoPath("https://cache.libria.fun/videos/media/ts/9876/1/720.mp4:hls:manifest.m3u8");

        Assert.Equal(string.Empty, metadata.OriginalTitle);
        Assert.Equal(string.Empty, metadata.TitleRu);
    }

    [Fact]
    public void PlayerViewModel_LoadVideo_WithExplicitMetadata_PreservesMetadata()
    {
        var settings = new AppSettings();
        var mockSettings = new Moq.Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);
        var mockLocalizer = new Moq.Mock<ILocalizer>();
        var resolver = new FilenamePlayerMediaMetadataResolver();

        var vm = new Kiriha.Mpv.UI.ViewModels.Player.PlayerViewModel(
            @"C:\Anime\dummy.mkv",
            null,
            resolver,
            mockSettings.Object,
            mockLocalizer.Object);

        var explicitMeta = new PlayerMediaMetadata(
            "Sousou no Frieren - 01",
            "Провожающая в последний путь Фрирен",
            "Frieren: Beyond Journey's End",
            "01",
            52991,
            "Sousou no Frieren");

        vm.LoadVideo("https://cache.libria.fun/videos/media/ts/9876/1/720.mp4:hls:manifest.m3u8", null, explicitMeta);

        Assert.Equal("Sousou no Frieren", vm.AnimeTitleRomaji);
        Assert.Equal("Провожающая в последний путь Фрирен", vm.AnimeTitleRu);
        Assert.Equal(52991, vm.AnimeId);
    }

    [Fact]
    public void PlayerMediaMetadataResolver_RomanNumeralSequel_MatchesNormalizedSeason2InDb()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"Kiriha_Test_Player_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var factory = new TestDbContextFactory(options);

        try
        {
            using (var db = new AppDbContext(options))
            {
                db.Database.EnsureCreated();
                db.UserAnime.Add(new AnimeEntity { Id = 49891, Title = "Tensei shitara Ken deshita" });
                db.UserAnime.Add(new AnimeEntity { Id = 53913, Title = "Tensei shitara Ken deshita 2nd Season" });
                db.SaveChanges();
            }

            var resolver = new PlayerMediaMetadataResolver(factory);
            var meta = resolver.Resolve(@"C:\Anime\[Erai-raws] Tensei Shitara Ken Deshita II - 04 [1080p].mkv");

            Assert.Equal(53913, meta.AnimeId);
            Assert.Equal("04", meta.EpisodeText);
        }
        finally
        {
            try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public void PlayerMediaMetadataResolver_RomanNumeralSequel_DoesNotFallbackToSeason1InDb()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"Kiriha_Test_Player_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var factory = new TestDbContextFactory(options);

        try
        {
            using (var db = new AppDbContext(options))
            {
                db.Database.EnsureCreated();
                db.UserAnime.Add(new AnimeEntity { Id = 49891, Title = "Tensei shitara Ken deshita" });
                db.SaveChanges();
            }

            var resolver = new PlayerMediaMetadataResolver(factory);
            var meta = resolver.Resolve(@"C:\Anime\[Erai-raws] Tensei Shitara Ken Deshita II - 04 [1080p].mkv");

            Assert.Null(meta.AnimeId);
            Assert.Equal("04", meta.EpisodeText);
        }
        finally
        {
            try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public void PlayerViewModel_ApplyExternalMetadata_UnlinkClearsTitlesAndId()
    {
        var settings = new AppSettings();
        settings.UI.UseRussianTitles = true;
        var mockSettings = new Moq.Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);
        var mockLocalizer = new Moq.Mock<ILocalizer>();

        var vm = new Kiriha.Mpv.UI.ViewModels.Player.PlayerViewModel(
            @"C:\Anime\frieren_01.mkv",
            null,
            null,
            mockSettings.Object,
            mockLocalizer.Object);

        vm.ApplyExternalMetadata(new PlayerMediaMetadata(
            "frieren_01",
            "Провожающая в последний путь Фрирен",
            "Frieren: Beyond Journey's End",
            "01",
            52991,
            "Sousou no Frieren"));

        Assert.Equal("Провожающая в последний путь Фрирен", vm.TopTitle);
        Assert.True(vm.HasBottomTitle);

        // Simulate ClearPlayerMetadata (Unlink)
        vm.ApplyExternalMetadata(new PlayerMediaMetadata(
            "frieren_01",
            string.Empty,
            string.Empty,
            "01",
            null,
            "frieren"));

        Assert.Equal("frieren", vm.TopTitle);
        Assert.False(vm.HasBottomTitle);
        Assert.Equal(string.Empty, vm.AnimeTitleRu);
        Assert.Equal(string.Empty, vm.AnimeTitleEn);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AppDbContext> options)
        {
            _options = options;
        }

        public AppDbContext CreateDbContext() => new(_options);
    }
}
