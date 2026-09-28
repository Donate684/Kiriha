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
}
