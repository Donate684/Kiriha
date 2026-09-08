using Kiriha.Core.Abstractions.Services;
using Kiriha.Core;
using Kiriha.Core.Domain.Models;
using Kiriha.Mpv.UI.Services.Player;
using Kiriha.Infrastructure.Player;
using Kiriha.Models;
using Kiriha.Core.Domain.Models.Entities;

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
        Assert.Equal("Frieren: Beyond Journey's End", vm.BottomTitle);
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
}
