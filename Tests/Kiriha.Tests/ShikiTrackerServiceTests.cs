using System.Text.Json;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Auth;
using Moq;

namespace Kiriha.Tests;

public sealed class ShikiTrackerServiceTests
{
    [Theory]
    [InlineData("watching", UserAnimeStatus.Watching)]
    [InlineData("completed", UserAnimeStatus.Completed)]
    [InlineData("on_hold", UserAnimeStatus.OnHold)]
    [InlineData("dropped", UserAnimeStatus.Dropped)]
    [InlineData("planned", UserAnimeStatus.PlanToWatch)]
    [InlineData("rewatching", UserAnimeStatus.Watching)]
    [InlineData("unknown_status", UserAnimeStatus.None)]
    [InlineData(null, UserAnimeStatus.None)]
    public void StatusMapper_MapsShikiStatuses_Correctly(string? shikiStatus, UserAnimeStatus expected)
    {
        Assert.Equal(expected, StatusMapper.FromShiki(shikiStatus));
    }

    [Fact]
    public void ShikiMapper_UsesMalId_WhenProvidedInAnime()
    {
        const string json = """
        {
            "id": 10001,
            "user_id": 42,
            "target_id": 52991,
            "target_type": "Anime",
            "score": 9,
            "status": "completed",
            "text": "Great show",
            "episodes": 28,
            "rewatches": 1,
            "anime": {
                "id": 52991,
                "malId": 52991,
                "name": "Sousou no Frieren",
                "russian": "Frieren",
                "image": {
                    "original": "/system/animes/original/52991.jpg"
                },
                "score": "9.13",
                "status": "released",
                "episodes": 28,
                "kind": "tv"
            }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        int capturedPrimary = 0;
        int capturedShiki = 0;

        var entity = ShikiMapper.MapRateToAnimeEntity(
            doc.RootElement,
            "https://shikimori.one",
            (primary, shiki) =>
            {
                capturedPrimary = primary;
                capturedShiki = shiki;
            });

        Assert.Equal(52991, entity.Id);
        Assert.Equal(52991, capturedPrimary);
        Assert.Equal(52991, capturedShiki);
        Assert.Equal("Sousou no Frieren", entity.Title);
        Assert.Equal("Frieren", entity.RussianTitle);
        Assert.Equal("https://shikimori.one/system/animes/original/52991.jpg", entity.MainPictureUrl);
        Assert.Equal(UserAnimeStatus.Completed, entity.Status);
        Assert.Equal(28, entity.Progress);
        Assert.Equal(28, entity.TotalEpisodes);
        Assert.Equal("9", entity.Score);
        Assert.Equal("9.13", entity.MeanScore);
        Assert.Equal(1, entity.RewatchCount);
        Assert.Equal("Great show", entity.Notes);
        Assert.Equal(MediaKind.Anime, entity.MediaKind);
    }

    [Fact]
    public void ShikiMapper_UsesShikiId_WhenMalIdAbsent()
    {
        const string json = """
        {
            "id": 10002,
            "user_id": 42,
            "target_id": 999999,
            "target_type": "Anime",
            "score": 8,
            "status": "watching",
            "episodes": 3,
            "anime": {
                "id": 999999,
                "name": "Exclusive Donghua",
                "russian": "Exclusive Donghua RU",
                "episodes": 12,
                "kind": "ona"
            }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        int capturedPrimary = 0;
        int capturedShiki = 0;

        var entity = ShikiMapper.MapRateToAnimeEntity(
            doc.RootElement,
            "https://shikimori.one",
            (primary, shiki) =>
            {
                capturedPrimary = primary;
                capturedShiki = shiki;
            });

        Assert.Equal(999999, entity.Id);
        Assert.Equal(999999, capturedPrimary);
        Assert.Equal(999999, capturedShiki);
        Assert.Equal("Exclusive Donghua", entity.Title);
        Assert.Equal(UserAnimeStatus.Watching, entity.Status);
        Assert.Equal(3, entity.Progress);
        Assert.Equal(12, entity.TotalEpisodes);
    }

    [Fact]
    public void ShikiMapper_MapsManga_Correctly()
    {
        const string json = """
        {
            "id": 20001,
            "target_id": 1,
            "target_type": "Manga",
            "score": 10,
            "status": "watching",
            "chapters": 100,
            "volumes": 10,
            "manga": {
                "id": 1,
                "mal_id": 1,
                "name": "Monster",
                "russian": "Monster",
                "kind": "manga",
                "chapters": 162,
                "volumes": 18
            }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var entity = ShikiMapper.MapRateToAnimeEntity(doc.RootElement, "https://shikimori.one");

        Assert.Equal(1, entity.Id);
        Assert.Equal(MediaKind.Manga, entity.MediaKind);
        Assert.Equal("Monster", entity.Title);
        Assert.Equal(100, entity.ChaptersRead);
        Assert.Equal(10, entity.VolumesRead);
        Assert.Equal(162, entity.Chapters);
        Assert.Equal(18, entity.Volumes);
        Assert.Equal("10", entity.Score);
    }

    [Fact]
    public void ShikiMapper_MapsAnimeRates_WithoutTargetType()
    {
        const string json = """
        {
            "id": 2002,
            "score": 8,
            "status": "watching",
            "text": "Great anime",
            "episodes": 12,
            "rewatches": 0,
            "anime": {
                "id": 62001,
                "name": "Yomi no Tsugai",
                "russian": "Цугаи загробного мира",
                "image": {
                    "original": "/uploads/poster/animes/62001/poster.jpeg"
                },
                "score": "8.50",
                "status": "ongoing",
                "episodes": 24,
                "kind": "tv"
            },
            "manga": null
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var entity = ShikiMapper.MapRateToAnimeEntity(doc.RootElement, "https://shikimori.net");

        Assert.Equal(62001, entity.Id);
        Assert.Equal(MediaKind.Anime, entity.MediaKind);
        Assert.Equal("Yomi no Tsugai", entity.Title);
        Assert.Equal("Цугаи загробного мира", entity.RussianTitle);
        Assert.Equal("https://shikimori.net/uploads/poster/animes/62001/poster.jpeg", entity.MainPictureUrl);
        Assert.Equal(UserAnimeStatus.Watching, entity.Status);
        Assert.Equal(12, entity.Progress);
        Assert.Equal(24, entity.TotalEpisodes);
        Assert.Equal("8", entity.Score);
        Assert.Equal("8.50", entity.MeanScore);
    }

    [Fact]
    public void ShikiMapper_MapsMangaRates_WithoutTargetType()
    {
        const string json = """
        {
            "id": 105629474,
            "score": 9,
            "status": "completed",
            "chapters": 136,
            "volumes": 22,
            "anime": null,
            "manga": {
                "id": 73,
                "name": "Tenjou Tenge",
                "russian": "Небо и земля",
                "image": {
                    "original": "/uploads/poster/mangas/73/poster.jpeg"
                },
                "score": "7.25",
                "status": "released",
                "volumes": 22,
                "chapters": 136,
                "kind": "manga"
            }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var entity = ShikiMapper.MapRateToAnimeEntity(doc.RootElement, "https://shikimori.net");

        Assert.Equal(73, entity.Id);
        Assert.Equal(MediaKind.Manga, entity.MediaKind);
        Assert.Equal("Tenjou Tenge", entity.Title);
        Assert.Equal("Небо и земля", entity.RussianTitle);
        Assert.Equal("https://shikimori.net/uploads/poster/mangas/73/poster.jpeg", entity.MainPictureUrl);
        Assert.Equal(UserAnimeStatus.Completed, entity.Status);
        Assert.Equal(136, entity.ChaptersRead);
        Assert.Equal(22, entity.VolumesRead);
        Assert.Equal(136, entity.Chapters);
        Assert.Equal(22, entity.Volumes);
    }

    private sealed class DelegatingHandlerStub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }

    [Fact]
    public async Task EnrichMissingPostersFromGraphQlAsync_PopulatesMissingPosters()
    {
        var handler = new DelegatingHandlerStub(req =>
        {
            var json = """
            {
                "data": {
                    "animes": [
                        {
                            "id": "62485",
                            "poster": {
                                "originalUrl": "https://shikimori.io/uploads/poster/animes/62485/731f400945ffd21b4ae5d72b0bfcb1dd.jpeg"
                            }
                        }
                    ]
                }
            }
            """;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler);
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.Current).Returns(new AppSettings());
        var tokenService = new ShikiTokenService(settingsMock.Object, null!);
        var cacheRepoMock = new Mock<IHttpCacheRepository>();

        var service = new ShikiApiService(
            client,
            settingsMock.Object,
            tokenService,
            new ShikiHostResolver(),
            cacheRepoMock.Object,
            new ShikiRateLimiter(),
            ShikiMirror.One);

        var items = new List<AnimeEntity>
        {
            new() { Id = 62485, Title = "Kanojo, Okarishimasu 5th Season", MainPictureUrl = null },
            new() { Id = 100, Title = "Existing", MainPictureUrl = "https://shikimori.one/assets/poster.jpg" }
        };

        await service.EnrichMissingPostersFromGraphQlAsync(items, false, CancellationToken.None);

        Assert.Equal("https://shikimori.io/uploads/poster/animes/62485/731f400945ffd21b4ae5d72b0bfcb1dd.jpeg", items[0].MainPictureUrl);
        Assert.Equal("https://shikimori.one/assets/poster.jpg", items[1].MainPictureUrl);
    }
}
