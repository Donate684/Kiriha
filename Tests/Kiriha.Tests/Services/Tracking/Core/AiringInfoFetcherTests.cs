using System;
using System.Threading.Tasks;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Core;
using Kiriha.Models;
using Moq;
using Xunit;

namespace Kiriha.Tests.Services.Tracking.Core;

public class AiringInfoFetcherTests
{
    [Fact]
    public void ResolveAired_NextEpisodeInFuture_DoesNotIncrementAired()
    {
        var anime = new AnimeEntity { EpisodesAired = 5 };
        var airing = new AniListAiringInfo(1, 1, null, 7, DateTime.UtcNow.AddDays(1), null);

        var (aired, nextSlot) = AiringInfoFetcher.ResolveAired(anime, airing);

        Assert.Equal(6, aired); // Next is 7, so aired is 6
        Assert.Equal(airing.NextEpisodeAt, nextSlot);
    }

    [Fact]
    public void ResolveAired_NextEpisodeInPast_IncrementsAired()
    {
        var anime = new AnimeEntity { EpisodesAired = 5 };
        var airing = new AniListAiringInfo(1, 1, null, 7, DateTime.UtcNow.AddDays(-1), null);

        var (aired, nextSlot) = AiringInfoFetcher.ResolveAired(anime, airing);

        Assert.Equal(7, aired);
        Assert.Null(nextSlot);
    }

    [Fact]
    public void ResolveAired_FinishedStatus_SetsTotalEpisodes()
    {
        var anime = new AnimeEntity { EpisodesAired = 5 };
        var airing = new AniListAiringInfo(1, 1, "FINISHED", null, null, 12);

        var (aired, nextSlot) = AiringInfoFetcher.ResolveAired(anime, airing);

        Assert.Equal(12, aired);
        Assert.Null(nextSlot);
    }

    [Fact]
    public void ResolveAired_NoAiringInfo_UsesAnimeNextEpisodeAt()
    {
        var anime = new AnimeEntity 
        { 
            EpisodesAired = 5,
            NextEpisodeAt = DateTime.UtcNow.AddDays(-1)
        };
        var airing = new AniListAiringInfo(1, 1, null, null, null, null);

        var (aired, nextSlot) = AiringInfoFetcher.ResolveAired(anime, airing);

        Assert.Equal(6, aired);
        Assert.Null(nextSlot);
    }

    [Fact]
    public async System.Threading.Tasks.Task FetchAndResolveAsync_ShikimoriSource_CallsShikiApi()
    {
        var aniListMock = new Moq.Mock<IAniListApiService>();
        var shikiMock = new Moq.Mock<IShikiApiService>();
        var settingsMock = new Moq.Mock<ISettingsService>();

        var settings = new AppSettings();
        settings.System.AiringSource = EpisodeAiringSource.Shikimori;
        settingsMock.Setup(s => s.Current).Returns(settings);

        shikiMock.Setup(s => s.GetAiringInfoAsync(123, false, default))
            .ReturnsAsync(new EpisodeAiringInfo(123, 123, "ongoing", 8, DateTime.UtcNow.AddDays(1), 12, 7));

        var fetcher = new AiringInfoFetcher(aniListMock.Object, shikiMock.Object, settingsMock.Object);
        var anime = new AnimeEntity { Id = 123, EpisodesAired = 7 };

        var (airing, aired, nextSlot) = await fetcher.FetchAndResolveAsync(anime, false, default);

        Assert.NotNull(airing);
        Assert.Equal(7, aired);
        shikiMock.Verify(s => s.GetAiringInfoAsync(123, false, default), Moq.Times.Once);
        aniListMock.Verify(s => s.GetNextAiringAsync(Moq.It.IsAny<int>(), Moq.It.IsAny<bool>(), default), Moq.Times.Never);
    }

    [Fact]
    public async System.Threading.Tasks.Task FetchAndResolveAsync_AniListSource_CallsAniListApi()
    {
        var aniListMock = new Moq.Mock<IAniListApiService>();
        var shikiMock = new Moq.Mock<IShikiApiService>();
        var settingsMock = new Moq.Mock<ISettingsService>();

        var settings = new AppSettings();
        settings.System.AiringSource = EpisodeAiringSource.AniList;
        settingsMock.Setup(s => s.Current).Returns(settings);

        aniListMock.Setup(s => s.GetNextAiringAsync(123, false, default))
            .ReturnsAsync(new AniListAiringInfo(1, 123, null, 8, DateTime.UtcNow.AddDays(1), 12));

        var fetcher = new AiringInfoFetcher(aniListMock.Object, shikiMock.Object, settingsMock.Object);
        var anime = new AnimeEntity { Id = 123, EpisodesAired = 7 };

        var (airing, aired, nextSlot) = await fetcher.FetchAndResolveAsync(anime, false, default);

        Assert.NotNull(airing);
        Assert.Equal(7, aired);
        aniListMock.Verify(s => s.GetNextAiringAsync(123, false, default), Moq.Times.Once);
        shikiMock.Verify(s => s.GetAiringInfoAsync(Moq.It.IsAny<int>(), Moq.It.IsAny<bool>(), default), Moq.Times.Never);
    }
}
