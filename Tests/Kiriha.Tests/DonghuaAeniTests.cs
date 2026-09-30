using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.ViewModels.Seasonal;
using Moq;
using Xunit;

namespace Kiriha.Tests;

public class DonghuaAeniTests
{
    [Fact]
    public void DonghuaDetector_DetectsChineseStudios()
    {
        var anime = new AnimeEntity
        {
            Id = 60597,
            Title = "Da Wang Rao Ming 3",
            Studios = new List<string> { "Big Firebird Culture" }
        };

        var result = DonghuaDetector.Detect(anime);
        Assert.Equal("CN", result);
    }

    [Fact]
    public void DonghuaDetector_DetectsBilibiliSynopsis()
    {
        var anime = new AnimeEntity
        {
            Id = 62945,
            Title = "Di Yi Xulie 2",
            Synopsis = "Survival depends on blood and fire.\n\n(Source: Bilibili, translated)"
        };

        var result = DonghuaDetector.Detect(anime);
        Assert.Equal("CN", result);
    }

    [Fact]
    public void DonghuaDetector_DetectsKoreanStudio()
    {
        var anime = new AnimeEntity
        {
            Id = 99999,
            Title = "Lookism",
            Studios = new List<string> { "Studio Mir" }
        };

        var result = DonghuaDetector.Detect(anime);
        Assert.Equal("KR", result);
    }

    [Fact]
    public void DonghuaDetector_DoesNotTriggerOnJapaneseAnime()
    {
        var anime = new AnimeEntity
        {
            Id = 61990,
            Title = "Cyberpunk: Edgerunners 2",
            JapaneseTitle = "サイバーパンク エッジランナーズ2",
            Studios = new List<string> { "Trigger" },
            Synopsis = "A sequel to the acclaimed anime series."
        };

        var result = DonghuaDetector.Detect(anime);
        Assert.Null(result);
    }

    [Fact]
    public void SeasonalCategoryBuckets_SeparatesDonghuaAndAeni()
    {
        var localizerMock = new Mock<ILocalizer>();
        localizerMock.Setup(l => l.GetLoc(It.IsAny<string>()))
            .Returns<string>(key => key switch
            {
                "anime.seasonal.categories.new" => "Новые",
                "anime.seasonal.categories.continuing" => "Продолжающиеся",
                "anime.seasonal.categories.movies" => "Фильмы",
                "ova" => "OVA",
                "ona" => "ONA",
                "anime.seasonal.categories.specials" => "Спешлы",
                "anime.seasonal.categories.other" => "Прочее",
                "anime.seasonal.categories.donghua" => "Дунхуа",
                "anime.seasonal.categories.aeni" => "Эни",
                _ => key
            });
        localizerMock.Setup(l => l.GetLoc(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns<string, object[]>((k, args) => $"{args[0]} ({args[1]})");

        var items = new List<AnimeEntity>
        {
            new AnimeEntity { Id = 1, Title = "Normal ONA", Type = "ONA", CountryOfOrigin = "JP" },
            new AnimeEntity { Id = 2, Title = "Donghua ONA", Type = "ONA", CountryOfOrigin = "CN" },
            new AnimeEntity { Id = 3, Title = "Aeni ONA", Type = "ONA", CountryOfOrigin = "KR" },
            new AnimeEntity { Id = 4, Title = "Normal TV", Type = "TV", StartYear = 2026, StartSeason = "fall", CountryOfOrigin = "JP" },
            new AnimeEntity { Id = 5, Title = "Donghua TV", Type = "TV", StartYear = 2026, StartSeason = "fall", CountryOfOrigin = "CN" }
        };

        var buckets = SeasonalCategoryBuckets.Build(items, 2026, "fall");

        var donghuaItems = buckets.GetItems("Donghua");
        var aeniItems = buckets.GetItems("Aeni");
        var onaItems = buckets.GetItems("ONA");
        var newItems = buckets.GetItems("New");

        Assert.Equal(2, donghuaItems.Count);
        Assert.Contains(donghuaItems, x => x.Id == 2);
        Assert.Contains(donghuaItems, x => x.Id == 5);

        Assert.Single(aeniItems);
        Assert.Equal(3, aeniItems[0].Id);

        Assert.Single(onaItems);
        Assert.Equal(1, onaItems[0].Id);

        Assert.Single(newItems);
        Assert.Equal(4, newItems[0].Id);

        var headers = buckets.BuildHeaders(localizerMock.Object);
        Assert.Equal("Дунхуа (2)", headers["Donghua"]);
        Assert.Equal("Эни (1)", headers["Aeni"]);
        Assert.Equal("ONA (1)", headers["ONA"]);
        Assert.Equal("Новые (1)", headers["New"]);
    }

    [Fact]
    public void DonghuaDetector_DetectsHangulInTitle_AsKorean()
    {
        var anime = new AnimeEntity
        {
            Id = 51000,
            Title = "Lookism",
            JapaneseTitle = "외모지상주의"
        };

        var result = DonghuaDetector.Detect(anime);
        Assert.Equal("KR", result);
    }

    [Fact]
    public void DonghuaDetector_DetectsSimplifiedChineseHanzi_AsChinese()
    {
        var anime = new AnimeEntity
        {
            Id = 38000,
            Title = "Doupo Cangqiong",
            JapaneseTitle = "斗破苍穹"
        };

        var result = DonghuaDetector.Detect(anime);
        Assert.Equal("CN", result);
    }

    [Fact]
    public void DonghuaDetector_DetectsPinyinKeyword_AsChinese()
    {
        var anime = new AnimeEntity
        {
            Id = 55000,
            Title = "Xian Ni: Renegade Immortal"
        };

        var result = DonghuaDetector.Detect(anime);
        Assert.Equal("CN", result);
    }

    [Fact]
    public void DonghuaDetector_DetectsNativeChineseTitle_Fanren()
    {
        var anime = new AnimeEntity
        {
            Id = 56000,
            Title = "Record of a Mortal's Journey to Immortality",
            JapaneseTitle = "凡人修仙传"
        };

        var result = DonghuaDetector.Detect(anime);
        Assert.Equal("CN", result);
    }

    [Fact]
    public void DonghuaDetector_DoesNotTriggerOnJapanesePureKanji_JujutsuKaisen()
    {
        var anime = new AnimeEntity
        {
            Id = 40748,
            Title = "Jujutsu Kaisen",
            JapaneseTitle = "呪術廻戦",
            Studios = new List<string> { "MAPPA" }
        };

        var result = DonghuaDetector.Detect(anime);
        Assert.Null(result);
    }

    [Fact]
    public void SeasonalCategoryBuckets_SelfHeals_WhenCountryIsMissingOrJp()
    {
        var localizerMock = new Mock<ILocalizer>();
        localizerMock.Setup(l => l.GetLoc(It.IsAny<string>()))
            .Returns<string>(key => key);
        localizerMock.Setup(l => l.GetLoc(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns<string, object[]>((k, args) => $"{args[0]} ({args[1]})");

        var items = new List<AnimeEntity>
        {
            // Missing country, but has Chinese studio
            new AnimeEntity { Id = 10, Title = "Soul Land", Type = "ONA", Studios = new List<string> { "Sparkly Key Animation" } },
            // Stale "JP" country in cache, but is clearly Korean Aeni
            new AnimeEntity { Id = 11, Title = "Lookism", Type = "ONA", JapaneseTitle = "외모지상주의", CountryOfOrigin = "JP" }
        };

        var buckets = SeasonalCategoryBuckets.Build(items, 2026, "fall");

        var donghua = buckets.GetItems("Donghua");
        var aeni = buckets.GetItems("Aeni");

        Assert.Single(donghua);
        Assert.Equal(10, donghua[0].Id);
        Assert.Equal("CN", items[0].CountryOfOrigin);

        Assert.Single(aeni);
        Assert.Equal(11, aeni[0].Id);
        Assert.Equal("KR", items[1].CountryOfOrigin);
    }

    [Fact]
    public void DonghuaDetector_DetectsMal64383_DaTangYaoTan()
    {
        var anime = new AnimeEntity
        {
            Id = 64383,
            Title = "Da Tang Yao Tan",
            EnglishTitle = "Mystery of Chang'an",
            JapaneseTitle = "大唐妖探",
            AlternativeTitles = new List<string> { "Demon Agent", "长安探案录", "Chang'an Tan'an Lu" },
            Synopsis = "At the dawn of a prosperous era, the capital city of Chang'an appears to be a place where humans and demons coexist... (Source: Bangumi, translated)"
        };

        var result = DonghuaDetector.Detect(anime);
        Assert.Equal("CN", result);
    }

    [Fact]
    public void DonghuaDetector_DetectsMal64394_ShixiongAShixiong()
    {
        var anime = new AnimeEntity
        {
            Id = 64394,
            Title = "Shixiong A Shixiong: Zuizhong Ji",
            EnglishTitle = "Big Brother Final Season",
            JapaneseTitle = "师兄啊师兄 最终季",
            Studios = new List<string> { "Sparkly Key Animation Studio" },
            Synopsis = "Final season of Shixiong A Shixiong."
        };

        var result = DonghuaDetector.Detect(anime);
        Assert.Equal("CN", result);
    }
}

