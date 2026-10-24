using Kiriha.Utils.Parsing;

namespace Kiriha.Tests;

public sealed class AnimeParseCacheTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_BlankFilenameReturnsEmpty(string? filename)
    {
        var result = AnimeParseCache.Parse(filename);
        Assert.Same(ParsedAnimeRelease.Empty, result);
        Assert.Equal(string.Empty, result.Title);
        Assert.Equal(string.Empty, result.Episode);
    }

    [Fact]
    public void Parse_ClassicNyaaRelease()
    {
        var parsed = AnimeParseCache.Parse("[SubsPlease] Sousou no Frieren - 12 (1080p) [F1A2B3C4].mkv");

        Assert.Equal("Sousou no Frieren", parsed.Title);
        Assert.Equal("12", parsed.Episode);
        Assert.Equal("1080p", parsed.Resolution);
        Assert.Equal("SubsPlease", parsed.ReleaseGroup);
    }

    [Fact]
    public void Parse_VersionAndMultipleSubtitles()
    {
        var parsed = AnimeParseCache.Parse("[Erai-raws] Kusuriya no Hitorigoto - 05v2 [1080p][Multiple Subtitle][ABCD1234].mkv");

        Assert.Equal("Kusuriya no Hitorigoto", parsed.Title);
        Assert.Equal("05", parsed.Episode);
        Assert.Equal("1080p", parsed.Resolution);
        Assert.Equal("Erai-raws", parsed.ReleaseGroup);
    }

    [Fact]
    public void Parse_SxxExxAndSeason()
    {
        var parsed = AnimeParseCache.Parse("[Erai-raws] Oshi no Ko - S02E03 [1080p]");

        Assert.Equal("Oshi no Ko", parsed.Title);
        Assert.Equal(2, parsed.Season);
        Assert.Equal("03", parsed.Episode);
        Assert.Equal("1080p", parsed.Resolution);
        Assert.Equal("Erai-raws", parsed.ReleaseGroup);
    }

    [Fact]
    public void Parse_SceneDottedWebDl_WithEpisodeTitle()
    {
        var parsed = AnimeParseCache.Parse("Dandadan.S01E01.That.Is.How.Love.Starts.Ya.Know.2160p.NF.WEB-DL.DDP2.0.AV1-ToonsHub.mkv");

        Assert.Equal("Dandadan", parsed.Title);
        Assert.Equal(1, parsed.Season);
        Assert.Equal("01", parsed.Episode);
        Assert.Equal("That Is How Love Starts Ya Know", parsed.EpisodeTitle);
        Assert.Equal("2160p", parsed.Resolution);
        Assert.Equal("ToonsHub", parsed.ReleaseGroup);
    }

    [Fact]
    public void Parse_SceneDottedWebDl_Mashle()
    {
        var parsed = AnimeParseCache.Parse("Mashle.Magic.and.Muscles.S02E05.1080p.CR.WEB-DL.AAC2.0.H.264-VARYG.mkv");

        Assert.Equal("Mashle Magic and Muscles", parsed.Title);
        Assert.Equal(2, parsed.Season);
        Assert.Equal("05", parsed.Episode);
        Assert.Equal("1080p", parsed.Resolution);
        Assert.Equal("VARYG", parsed.ReleaseGroup);
    }

    [Fact]
    public void Parse_ComplexTitle_NumbersAndHyphens()
    {
        var parsed = AnimeParseCache.Parse("[SubsPlease] 86 - Eighty Six - 07 (1080p) [12345678].mkv");

        Assert.Equal("86 - Eighty Six", parsed.Title);
        Assert.Equal("07", parsed.Episode);
        Assert.Equal("1080p", parsed.Resolution);
        Assert.Equal("SubsPlease", parsed.ReleaseGroup);
    }

    [Fact]
    public void Parse_ComplexTitle_ReZero()
    {
        var parsed = AnimeParseCache.Parse("[SubsPlease] Re:Zero kara Hajimeru Isekai Seikatsu - 25 (1080p).mkv");

        Assert.Equal("Re:Zero kara Hajimeru Isekai Seikatsu", parsed.Title);
        Assert.Equal("25", parsed.Episode);
        Assert.Equal("1080p", parsed.Resolution);
    }

    [Fact]
    public void Parse_ComplexTitle_SteinsGate0()
    {
        var parsed = AnimeParseCache.Parse("[SubsPlease] Steins;Gate 0 - 08 (1080p).mkv");

        Assert.Equal("Steins;Gate 0", parsed.Title);
        Assert.Equal("08", parsed.Episode);
        Assert.Equal("1080p", parsed.Resolution);
    }

    [Fact]
    public void Parse_ComplexTitle_MobPsycho100II()
    {
        var parsed = AnimeParseCache.Parse("[SubsPlease] Mob Psycho 100 II - 04 (1080p).mkv");

        Assert.Equal("Mob Psycho 100 II", parsed.Title);
        Assert.Equal("04", parsed.Episode);
        Assert.Equal("1080p", parsed.Resolution);
    }

    [Fact]
    public void Parse_ComplexTitle_FateStayNightUnlimitedBladeWorks()
    {
        var parsed = AnimeParseCache.Parse("[u] Fate/stay night [Unlimited Blade Works] - 03 (BD 1080p).mkv");

        Assert.Equal("Fate/stay night [Unlimited Blade Works]", parsed.Title);
        Assert.Equal("03", parsed.Episode);
        Assert.Equal("1080p", parsed.Resolution);
        Assert.Equal("u", parsed.ReleaseGroup);
    }

    [Fact]
    public void Parse_ComplexTitle_BleachThousandYearBloodWar()
    {
        var parsed = AnimeParseCache.Parse("Bleach: Thousand-Year Blood War - The Conflict - 04 (1080p).mkv");

        Assert.Equal("Bleach: Thousand-Year Blood War - The Conflict", parsed.Title);
        Assert.Equal("04", parsed.Episode);
        Assert.Equal("1080p", parsed.Resolution);
    }

    [Fact]
    public void Parse_RussianRelease_AniLibriaSimple()
    {
        var parsed = AnimeParseCache.Parse("[AniLibria.TV] Sousou no Frieren [ТВ-1] - Серия 12 [1080p].mkv");

        Assert.Equal("Sousou no Frieren", parsed.Title);
        Assert.Equal(1, parsed.Season);
        Assert.Equal("12", parsed.Episode);
        Assert.Equal("1080p", parsed.Resolution);
        Assert.Equal("AniLibria.TV", parsed.ReleaseGroup);
    }

    [Fact]
    public void Parse_RussianRelease_AniLibriaBilingual()
    {
        var parsed = AnimeParseCache.Parse("[AniLibria.TV] Провожающая в последний путь Фрирен / Sousou no Frieren [ТВ-1] - Серия 12 [1080p].mkv");

        Assert.Equal("Sousou no Frieren", parsed.Title);
        Assert.Equal(1, parsed.Season);
        Assert.Equal("12", parsed.Episode);
        Assert.Equal("1080p", parsed.Resolution);
        Assert.Equal("AniLibria.TV", parsed.ReleaseGroup);
    }

    [Fact]
    public void Parse_RussianRelease_SoloLevelingTv2()
    {
        var parsed = AnimeParseCache.Parse("Поднятие уровня в одиночку [ТВ-2] [05 из 12] [1080p] [AniLibria]");

        Assert.Equal("Поднятие уровня в одиночку", parsed.Title);
        Assert.Equal(2, parsed.Season);
        Assert.Equal("05", parsed.Episode);
        Assert.Equal("1080p", parsed.Resolution);
        Assert.Equal("AniLibria", parsed.ReleaseGroup);
    }

    [Fact]
    public void Parse_ExternalSubtitleTrack()
    {
        var parsed = AnimeParseCache.Parse("[SubsPlease] Dandadan - 03 (1080p).en.ass");

        Assert.Equal("Dandadan", parsed.Title);
        Assert.Equal("03", parsed.Episode);
        Assert.Equal("1080p", parsed.Resolution);
        Assert.Equal("SubsPlease", parsed.ReleaseGroup);
    }

    [Fact]
    public void Parse_BatchRelease_Judas()
    {
        var parsed = AnimeParseCache.Parse("[Judas] Jujutsu Kaisen (Season 1) [01-24] [1080p][HEVC x265 10bit][Multi-Subs] (Batch)");

        Assert.Equal("Jujutsu Kaisen", parsed.Title);
        Assert.Equal(1, parsed.Season);
        Assert.True(parsed.IsBatch);
        Assert.Equal("01-24", parsed.Episode);
        Assert.Equal("1080p", parsed.Resolution);
        Assert.Equal("Judas", parsed.ReleaseGroup);
    }

    [Fact]
    public void Parse_FullmetalAlchemistBrotherhood()
    {
        var parsed = AnimeParseCache.Parse("Fullmetal Alchemist Brotherhood - 01");

        Assert.Equal("Fullmetal Alchemist Brotherhood", parsed.Title);
        Assert.Equal("01", parsed.Episode);
    }

    [Theory]
    [InlineData("Psycho-Pass", "Psycho-Pass")]
    [InlineData("K-On", "K-On")]
    [InlineData("Yu-Gi-Oh", "Yu-Gi-Oh")]
    [InlineData("Kaiju No. 8", "Kaiju No. 8")]
    [InlineData("Mob Psycho 100 II", "Mob Psycho 100 II")]
    [InlineData("Area 88", "Area 88")]
    public void Parse_StandaloneTitlesWithHyphensOrNumbers_DoNotLoseSuffixOrMisparseEpisode(
        string input,
        string expectedTitle)
    {
        var parsed = AnimeParseCache.Parse(input);

        Assert.Equal(expectedTitle, parsed.Title);
        Assert.Equal(string.Empty, parsed.Episode);
        Assert.Equal(string.Empty, parsed.ReleaseGroup);
    }

    [Theory]
    [InlineData("Fullmetal Alchemist Brotherhood 01", "Fullmetal Alchemist Brotherhood", "01")]
    [InlineData("Bleach 366 1080p", "Bleach", "366")]
    public void Parse_IsolatedEpisodeNumber_ExtractsWhenZeroPaddedOrFollowedByTechTag(
        string input,
        string expectedTitle,
        string expectedEpisode)
    {
        var parsed = AnimeParseCache.Parse(input);

        Assert.Equal(expectedTitle, parsed.Title);
        Assert.Equal(expectedEpisode, parsed.Episode);
    }
}
