using System;
using System.Text.Json;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Core;
using Xunit;

namespace Kiriha.Tests.Services.Tracking.Core;

public class ShikiAiringParserTests
{
    [Fact]
    public void ParseAiringInfo_OngoingAnime_ReturnsCorrectEpisodeAndDate()
    {
        var json = """
        {
            "id": 6149,
            "name": "Chibi Maruko-chan (1995)",
            "status": "ongoing",
            "episodes": 0,
            "episodes_aired": 1174,
            "next_episode_at": "2026-09-13T12:00:00.000+03:00"
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var airing = ShikiParser.ParseAiringInfo(doc.RootElement, 6149);

        Assert.NotNull(airing);
        Assert.Equal(6149, airing.SourceId);
        Assert.Equal(6149, airing.MalId);
        Assert.Equal("ongoing", airing.Status);
        Assert.Equal(1175, airing.NextEpisode);
        Assert.Equal(1174, airing.EpisodesAired);
        Assert.NotNull(airing.NextEpisodeAt);
        Assert.Equal(DateTimeKind.Utc, airing.NextEpisodeAt.Value.Kind);
        Assert.Equal(new DateTime(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc), airing.NextEpisodeAt.Value);
    }

    [Fact]
    public void ParseAiringInfo_ReleasedAnime_ReturnsFinishedStatus()
    {
        var json = """
        {
            "id": 55813,
            "name": "Mashle",
            "status": "released",
            "episodes": 12,
            "episodes_aired": 12,
            "next_episode_at": null
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var airing = ShikiParser.ParseAiringInfo(doc.RootElement, 55813);

        Assert.NotNull(airing);
        Assert.Equal("FINISHED", airing.Status);
        Assert.Null(airing.NextEpisode);
        Assert.Null(airing.NextEpisodeAt);
        Assert.Equal(12, airing.TotalEpisodes);
        Assert.Equal(12, airing.EpisodesAired);
    }

    [Fact]
    public void ParseAiringInfo_AnonsAnime_ReturnsNotYetReleasedStatus()
    {
        var json = """
        {
            "id": 99999,
            "name": "Upcoming Show",
            "status": "anons",
            "episodes": 24,
            "episodes_aired": 0,
            "next_episode_at": "2026-10-01T15:00:00.000Z"
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var airing = ShikiParser.ParseAiringInfo(doc.RootElement, 99999);

        Assert.NotNull(airing);
        Assert.Equal("NOT_YET_RELEASED", airing.Status);
        Assert.Equal(1, airing.NextEpisode);
        Assert.Equal(0, airing.EpisodesAired);
        Assert.Equal(24, airing.TotalEpisodes);
        Assert.NotNull(airing.NextEpisodeAt);
    }

    [Fact]
    public void ResolveAired_WithShikiOngoing_NextEpisodeInFuture_PreservesAiredCount()
    {
        var anime = new AnimeEntity { EpisodesAired = 10 };
        var airing = new EpisodeAiringInfo(
            SourceId: 100,
            MalId: 100,
            Status: "ongoing",
            NextEpisode: 11,
            NextEpisodeAt: DateTime.UtcNow.AddDays(2),
            TotalEpisodes: 12,
            EpisodesAired: 10);

        var (aired, nextSlot) = AiringInfoFetcher.ResolveAired(anime, airing);

        Assert.Equal(10, aired);
        Assert.Equal(airing.NextEpisodeAt, nextSlot);
    }

    [Fact]
    public void ResolveAired_WithShikiOngoing_NextEpisodeInPast_IncrementsAiredCount()
    {
        var anime = new AnimeEntity { EpisodesAired = 10 };
        var airing = new EpisodeAiringInfo(
            SourceId: 100,
            MalId: 100,
            Status: "ongoing",
            NextEpisode: 11,
            NextEpisodeAt: DateTime.UtcNow.AddHours(-1),
            TotalEpisodes: 12,
            EpisodesAired: 10);

        var (aired, nextSlot) = AiringInfoFetcher.ResolveAired(anime, airing);

        Assert.Equal(11, aired);
        Assert.Null(nextSlot);
    }

    [Fact]
    public void ResolveAired_WithShikiReleased_SetsTotalEpisodes()
    {
        var anime = new AnimeEntity { EpisodesAired = 10 };
        var airing = new EpisodeAiringInfo(
            SourceId: 100,
            MalId: 100,
            Status: "released",
            NextEpisode: null,
            NextEpisodeAt: null,
            TotalEpisodes: 12,
            EpisodesAired: 12);

        var (aired, nextSlot) = AiringInfoFetcher.ResolveAired(anime, airing);

        Assert.Equal(12, aired);
        Assert.Null(nextSlot);
    }
}
