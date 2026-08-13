using Kiriha.Core.Services;
using Kiriha.Core.Models;
using System;
using Kiriha.Models;
using Kiriha.Core.Abstractions.Models;
using Kiriha.Core.Abstractions.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Core;
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
}
