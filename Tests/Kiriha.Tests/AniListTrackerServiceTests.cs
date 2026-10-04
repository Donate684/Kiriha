using System.Text.Json;
using Moq;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Auth;

namespace Kiriha.Tests;

public sealed class AniListTrackerServiceTests
{
    [Fact]
    public void AniListApiService_IdentifiesAsAniListTracker_AndChecksEnabledState()
    {
        var mockCache = new Mock<IHttpCacheRepository>();
        var settings = new AppSettings();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(settings);

        using var client = new HttpClient();
        using var apiService = new AniListApiService(client, mockCache.Object, mockSettings.Object);

        Assert.Equal("AniList", apiService.Name);
        Assert.Equal(TrackerConstants.Ids.AniList, apiService.TrackerId);
        Assert.False(apiService.IsEnabled);

        // Add AniList account with tokens
        settings.Api.Accounts.Add(new TrackerAccount
        {
            TrackerId = TrackerConstants.Ids.AniList,
            Tokens = new AniListTokens { AccessToken = "valid_tok" }
        });

        Assert.True(apiService.IsEnabled);
    }

    [Theory]
    [InlineData("CURRENT", UserAnimeStatus.Watching)]
    [InlineData("COMPLETED", UserAnimeStatus.Completed)]
    [InlineData("PAUSED", UserAnimeStatus.OnHold)]
    [InlineData("DROPPED", UserAnimeStatus.Dropped)]
    [InlineData("PLANNING", UserAnimeStatus.PlanToWatch)]
    [InlineData("REPEATING", UserAnimeStatus.Watching)]
    public void StatusMapper_MapsAniListStatuses_Correctly(string aniStatus, UserAnimeStatus expected)
    {
        Assert.Equal(expected, StatusMapper.FromAniList(aniStatus));
    }

    [Fact]
    public void StatusMapper_ToAniList_MapsEnumToAniListStrings()
    {
        Assert.Equal("CURRENT", StatusMapper.ToAniList(UserAnimeStatus.Watching));
        Assert.Equal("COMPLETED", StatusMapper.ToAniList(UserAnimeStatus.Completed));
        Assert.Equal("PAUSED", StatusMapper.ToAniList(UserAnimeStatus.OnHold));
        Assert.Equal("DROPPED", StatusMapper.ToAniList(UserAnimeStatus.Dropped));
        Assert.Equal("PLANNING", StatusMapper.ToAniList(UserAnimeStatus.PlanToWatch));
        Assert.Equal("REPEATING", StatusMapper.ToAniList(UserAnimeStatus.Watching, isRewatching: true));
    }

    [Fact]
    public void AniListMapper_MapsMediaJsonToAnimeEntity_PreferringMalId()
    {
        const string json = """
        {
          "id": 16498,
          "idMal": 16498,
          "title": {
            "romaji": "Shingeki no Kyojin",
            "english": "Attack on Titan",
            "native": "進撃の巨人"
          },
          "coverImage": {
            "extraLarge": "https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx16498.jpg"
          },
          "description": "Centuries ago, mankind was slaughtered...",
          "episodes": 25,
          "format": "TV",
          "status": "FINISHED",
          "averageScore": 84,
          "popularity": 450000,
          "startDate": { "year": 2013, "month": 4, "day": 7 },
          "genres": ["Action", "Drama", "Fantasy"]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var entity = AniListMapper.MapMediaToAnimeEntity(doc.RootElement);

        Assert.Equal(16498, entity.Id);
        Assert.Equal("Attack on Titan", entity.Title);
        Assert.Equal(25, entity.TotalEpisodes);
        Assert.Equal(MediaKind.Anime, entity.MediaKind);
        Assert.Equal("8.40", entity.MeanScore);
        Assert.Equal(450000, entity.Popularity);
        Assert.Equal(new DateTime(2013, 4, 7, 0, 0, 0, DateTimeKind.Utc), entity.AiringDate);
        Assert.Contains("Action", entity.Genres);
    }

    [Fact]
    public void AniListMapper_MapsUserListEntryStatus()
    {
        const string mediaJson = """{ "id": 1, "idMal": 5114, "title": { "english": "Fullmetal Alchemist: Brotherhood" } }""";
        const string entryJson = """
        {
          "status": "CURRENT",
          "progress": 12,
          "score": 9.0,
          "repeat": 1,
          "notes": "Rewatching this masterpiece"
        }
        """;

        using var mediaDoc = JsonDocument.Parse(mediaJson);
        using var entryDoc = JsonDocument.Parse(entryJson);

        var entity = AniListMapper.MapMediaToAnimeEntity(mediaDoc.RootElement);
        AniListMapper.MapEntryToUserStatus(entryDoc.RootElement, entity);

        Assert.Equal(5114, entity.Id);
        Assert.Equal(UserAnimeStatus.Watching, entity.Status);
        Assert.Equal(12, entity.Progress);
        Assert.Equal("9", entity.Score);
        Assert.Equal(1, entity.RewatchCount);
        Assert.True(entity.IsRewatching);
        Assert.Equal("Rewatching this masterpiece", entity.Notes);
    }

    [Fact]
    public void AniListAuthService_GeneratesCorrectAuthUrl()
    {
        using var client = new HttpClient();
        var authService = new AniListAuthService(client);

        var url = authService.GetAuthUrl();
        Assert.Contains(AppConstants.Api.AniList.AuthUrl, url);
        Assert.Contains("client_id=", url);
        Assert.Contains("response_type=code", url);
    }
}
