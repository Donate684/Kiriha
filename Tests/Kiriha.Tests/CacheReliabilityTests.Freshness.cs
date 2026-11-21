using System.Net;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Auth;
using Kiriha.Core.Tracking.Mapping;
using Kiriha.Core.Tracking.Metadata;
using Kiriha.Data.Repository;
using Kiriha.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed partial class CacheReliabilityTests
{
    [Theory]
    [InlineData("anime")]
    [InlineData("manga")]
    [InlineData("shiki")]
    public async Task CatalogSearch_EmptyExpiresEarlierAndErrorsDoNotReplaceResults(string provider)
    {
        var (factory, repo, clock) = await CreateCacheAsync();
        var empty = provider == "shiki" ? "[]" : """{"data":[]}""";
        var found = provider == "shiki"
            ? """[{"id":42,"name":"Found"}]"""
            : """{"data":[{"node":{"id":42,"title":"Found","media_type":"manga"}}]}""";
        int requests = 0;
        using var http = new HttpClient(new ScriptedHandler((request, _) =>
        {
            switch (++requests)
            {
                case 1:
                    return Task.FromResult(Tagged(HttpStatusCode.OK, empty, "empty"));
                case 2:
                    Assert.Equal("W/\"empty\"", Assert.Single(request.Headers.IfNoneMatch).ToString());
                    return Task.FromResult(Tagged(HttpStatusCode.OK, found, "found"));
                case 3:
                    Assert.Equal("W/\"found\"", Assert.Single(request.Headers.IfNoneMatch).ToString());
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                default:
                    Assert.Equal("W/\"found\"", Assert.Single(request.Headers.IfNoneMatch).ToString());
                    return Json("""{"error":"Bad gateway"}""");
            }
        }));
        var settings = Settings();
        using var tokenManager = new MalTokenManager(settings, new MalAuthService(http));
        using var mal = new MalApiService(http, settings, tokenManager, repo, clock);
        using var rateLimiter = new ShikiRateLimiter();
        var clientFactory = new Mock<IHttpClientFactory>();
        clientFactory.Setup(x => x.CreateClient("ShikiClient")).Returns(http);
        using var shiki = new ShikiMetadataService(clientFactory.Object, settings, Mock.Of<IMetadataRepository>(),
            Mock.Of<IUserAnimeRepository>(), repo, new ShikiHostResolver(), Mock.Of<IUiDispatcher>(), rateLimiter, clock: clock);

        async Task<string?> SearchAsync() => provider switch
        {
            "shiki" => await shiki.ResolveRussianQueryAsync("Example"),
            "manga" => (await mal.SearchMangaAsync("Example")).SingleOrDefault()?.Title,
            _ => (await mal.SearchAnimeAsync("Example")).SingleOrDefault()?.Title
        };

        Assert.Null(await SearchAsync());
        clock.Advance(TimeSpan.FromSeconds(119));
        Assert.Null(await SearchAsync());
        Assert.Equal(1, requests);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("Found", await SearchAsync());
        var lastSuccess = clock.GetUtcNow().UtcDateTime;
        clock.Advance(TimeSpan.FromSeconds(599));
        Assert.Equal("Found", await SearchAsync());
        Assert.Equal(2, requests);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("Found", await SearchAsync());
        Assert.Equal("Found", await SearchAsync());
        Assert.Equal(4, requests);
        await using var db = factory.CreateDbContext();
        Assert.Equal(lastSuccess, (await db.HttpResponseCache.SingleAsync()).CreatedAt);
    }

    [Theory]
    [InlineData(11, 2026, "fall", 3)]
    [InlineData(11, 2027, "winter", 12)]
    [InlineData(11, 2026, "summer", 168)]
    [InlineData(12, 2027, "winter", 3)]
    [InlineData(12, 2026, "fall", 168)]
    [InlineData(12, 2027, "spring", 12)]
    [InlineData(1, 2026, "winter", 3)]
    public void SeasonalFreshness_UsesSameCalendarAcrossYearBoundary(int month, int year, string season, int hours)
    {
        var instant = new DateTime(2026, month, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(TimeSpan.FromHours(hours), SeasonalCacheStore.GetFreshness(year, season, instant));
    }

    [Theory]
    [InlineData(2026, "fall")]
    [InlineData(2027, "winter")]
    public async Task SeasonalSnapshot_RestartRetainsStaleDataAndEnrichmentKeepsValidationTime(int year, string season)
    {
        var start = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var clock = new VirtualClock(start);
        var root = _directory.FullName;
        var store = new SeasonalCacheStore(root, clock);
        await store.SaveAsync(year, season.ToUpperInvariant(), [new AnimeEntity { Id = 42, Title = "Original" }], validated: true);
        clock.Advance(TimeSpan.FromDays(40));

        var reopened = new SeasonalCacheStore(root, clock);
        var old = Assert.Single(reopened.LoadAll());
        Assert.Equal(season, old.Season);
        Assert.Equal(start.UtcDateTime, old.ValidatedAt);

        old.Items[0].RussianTitle = "Перевод";
        await reopened.SaveAsync(old.Year, old.Season, old.Items, catalogValidatedAt: old.ValidatedAt);
        var enriched = Assert.Single(reopened.LoadAll());
        Assert.Equal(start.UtcDateTime, enriched.ValidatedAt);
        Assert.Equal("Перевод", Assert.Single(enriched.Items).RussianTitle);

        var path = Path.Combine(root, $"{year}_{season}.json");
        var contents = await File.ReadAllBytesAsync(path);
        await reopened.SaveAsync(old.Year, old.Season, enriched.Items, validated: true);
        Assert.Equal(contents, await File.ReadAllBytesAsync(path));
        var renewed = Assert.Single(reopened.LoadAll());
        Assert.Equal(clock.GetUtcNow().UtcDateTime, renewed.ValidatedAt);

        old.Items[0].Title = "Late enrichment";
        await reopened.SaveAsync(old.Year, old.Season, old.Items, catalogValidatedAt: old.ValidatedAt);
        Assert.Equal("Original", Assert.Single(Assert.Single(reopened.LoadAll()).Items).Title);
    }

    [Fact]
    public async Task MalRecognition_RetriesConfirmedMissAfterOneDayInSameSession()
    {
        var (factory, repo, clock) = await CreateCacheAsync();
        int requests = 0;
        using var http = new HttpClient(new ScriptedHandler((_, _) => ++requests == 1
            ? Json("""{"data":[]}""")
            : Json("""{"data":[{"node":{"id":42,"title":"Frieren"}}]}""")));
        var settings = Settings();
        using var tokenManager = new MalTokenManager(settings, new MalAuthService(http));
        using var mal = new MalApiService(http, settings, tokenManager, repo, clock);
        var mapping = new MappingService(mal,
            new ManualMappingService(repository: null, legacyMappingFilePath: Path.Combine(_directory.FullName, "mapping.json")),
            new MalSearchCacheRepository(factory, clock), new RecognitionCache());

        Assert.Null(await mapping.SearchOnMalAsync("Frieren"));
        clock.Advance(TimeSpan.FromHours(24));
        Assert.Null(await mapping.SearchOnMalAsync("Frieren"));
        Assert.Equal(1, requests);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(42, await mapping.SearchOnMalAsync("Frieren"));
        Assert.Equal(2, requests);
    }
}
