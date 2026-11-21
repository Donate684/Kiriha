using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Auth;
using Kiriha.Core.Tracking.Mapping;
using Kiriha.Data.Core;
using Kiriha.Data.Repository;
using Kiriha.Services.Image;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed partial class CacheReliabilityTests : IDisposable
{
    private const string Url = "https://catalog.test/title";
    private static readonly TimeSpan Freshness = TimeSpan.FromHours(1);
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("kiriha-cache-");

    [Theory]
    [InlineData(HttpStatusCode.NotModified, "same")]
    [InlineData(HttpStatusCode.OK, "same")]
    [InlineData(HttpStatusCode.OK, "new-validator")]
    public async Task ValidatedUnchangedBody_RenewsFreshnessWithoutWritingBlob(HttpStatusCode status, string validator)
    {
        var (factory, repo, clock) = await CreateCacheAsync();
        int requests = 0;
        var body = new string('a', 512);
        using var http = new HttpClient(new ScriptedHandler((request, _) =>
        {
            if (++requests == 1) return Task.FromResult(Tagged(HttpStatusCode.OK, body, "same"));
            Assert.Equal("W/\"same\"", Assert.Single(request.Headers.IfNoneMatch).ToString());
            return Task.FromResult(Tagged(status, body, validator));
        }));
        var cache = new HttpConditionalCache(http, repo, "Test", clock);
        Assert.Equal(body, Encoding.UTF8.GetString(Assert.IsType<byte[]>((await SendAsync(cache)).Body)));

        await using var db = factory.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER forbid_body_rewrite
            BEFORE UPDATE OF Body ON http_response_cache
            BEGIN SELECT RAISE(ABORT, 'body should not be rewritten'); END;
            """, CancellationToken.None);
        var initial = await db.HttpResponseCache.SingleAsync(CancellationToken.None);
        clock.Advance(Freshness + TimeSpan.FromMinutes(1));

        var reopenedRepo = new HttpCacheRepository(factory, clock);
        var reopenedCache = new HttpConditionalCache(http, reopenedRepo, "Test", clock);
        Assert.NotNull((await SendAsync(reopenedCache)).Body);
        var refreshed = Assert.IsType<HttpCacheEntry>(await reopenedRepo.GetAsync(initial.UrlHash));
        Assert.Equal(clock.GetUtcNow().UtcDateTime, refreshed.CreatedAt);
        Assert.Equal(body, Encoding.UTF8.GetString(refreshed.Body));
        Assert.Equal(status == HttpStatusCode.NotModified ? "W/\"same\"" : $"W/\"{validator}\"", refreshed.ETag);
        Assert.True((await SendAsync(reopenedCache)).FromCache);
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task FailedValidation_PreservesOriginalAge(HttpStatusCode failure, bool serveStale)
    {
        var (factory, repo, clock) = await CreateCacheAsync();
        int requests = 0;
        using var http = new HttpClient(new ScriptedHandler((_, _) => Task.FromResult(++requests == 1
            ? Tagged(HttpStatusCode.OK, "good", "same")
            : new HttpResponseMessage(failure))));
        var cache = new HttpConditionalCache(http, repo, "Test", clock);
        var initialTime = clock.GetUtcNow().UtcDateTime;
        Assert.NotNull((await SendAsync(cache)).Body);
        clock.Advance(Freshness + TimeSpan.FromMinutes(1));

        var result = await SendAsync(cache);
        Assert.Equal(failure, result.StatusCode);
        Assert.Equal(serveStale, result.IsStaleFallback);
        Assert.Equal(serveStale ? "good" : null, result.Body is { } bytes ? Encoding.UTF8.GetString(bytes) : null);
        await using var db = factory.CreateDbContext();
        var saved = await db.HttpResponseCache.SingleOrDefaultAsync();
        Assert.Equal(serveStale ? initialTime : (DateTime?)null, saved?.CreatedAt);
    }

    [Fact]
    public async Task NoStoreResponse_RemovesPreviouslyPersistedBody()
    {
        var (factory, repo, clock) = await CreateCacheAsync();
        int requests = 0;
        using var http = new HttpClient(new ScriptedHandler((_, _) =>
        {
            if (++requests == 1) return Task.FromResult(Tagged(HttpStatusCode.OK, "old", "old"));
            var response = Tagged(HttpStatusCode.OK, "new", "new");
            response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            return Task.FromResult(response);
        }));
        var cache = new HttpConditionalCache(http, repo, "Test", clock);
        Assert.NotNull((await SendAsync(cache)).Body);
        clock.Advance(Freshness);
        Assert.False((await SendAsync(cache)).FromCache);
        await using var db = factory.CreateDbContext();
        Assert.Equal(0, await db.HttpResponseCache.CountAsync());
        Assert.False((await SendAsync(cache)).FromCache);
        Assert.Equal(3, requests);
    }

    [Fact]
    public async Task ConcurrentColdCache_CancellingFirstWaiterDoesNotRestartRequest()
    {
        var (_, repo, clock) = await CreateCacheAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int sends = 0;
        using var http = new HttpClient(new ScriptedHandler(async (_, ct) =>
        {
            Interlocked.Increment(ref sends);
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
            return Tagged(HttpStatusCode.OK, "first", "tag");
        }));
        var firstCache = new HttpConditionalCache(http, repo, "First", clock);
        var secondCache = new HttpConditionalCache(http, repo, "Second", clock);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var first = SendAsync(firstCache, cancel.Token);
        await started.Task.WaitAsync(timeout.Token);

        int unexpectedWork = 0;
        Task<HttpRequestMessage> MakeRequest(CancellationToken _)
        {
            Interlocked.Increment(ref unexpectedWork);
            return Task.FromResult(new HttpRequestMessage(HttpMethod.Get, Url));
        }
        Task<HttpCacheResult> Read(CancellationToken ct) =>
            secondCache.SendForResultAsync(Url, MakeRequest, ct: ct, localTtl: Freshness);

        var concurrent = Enumerable.Range(0, 12).Select(_ => Read(timeout.Token)).ToArray();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        release.SetResult();
        Assert.All(await Task.WhenAll(concurrent), result => Assert.NotNull(result.Body));
        Assert.True((await Read(timeout.Token)).FromCache);
        Assert.Equal(1, sends);
        Assert.Equal(0, unexpectedWork);
    }

    [Fact]
    public async Task MalCatalog_RepeatSearchUsesCacheAndPartialSeasonIsRejected()
    {
        var (_, repo, _) = await CreateCacheAsync();
        int searches = 0;
        int seasonPages = 0;
        using var http = new HttpClient(new ScriptedHandler((request, _) =>
        {
            var url = Assert.IsType<Uri>(request.RequestUri);
            Assert.DoesNotContain("my_list_status", url.Query);
            if (url.Query.Contains("q=cache", StringComparison.Ordinal))
            {
                searches++;
                return Json("""{"data":[{"node":{"id":12,"title":"Cache"}}]}""");
            }
            seasonPages++;
            if (url.Query == "?offset=100")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return Json("""
                {"data":[{"node":{"id":12,"title":"Cache"}}],
                 "paging":{"next":"https://api.myanimelist.net/v2/anime/season/2026/fall?offset=100"}}
                """);
        }));
        var settings = Settings();
        using var tokenManager = new MalTokenManager(settings, new MalAuthService(http));
        using var mal = new MalApiService(http, settings, tokenManager, repo);
        var ct = CancellationToken.None;

        Assert.Equal(12, Assert.Single(await mal.SearchAnimeAsync("cache", ct)).Id);
        Assert.Equal(12, Assert.Single(await mal.SearchAnimeAsync("cache", ct)).Id);
        Assert.Empty(await mal.GetSeasonalAnimeAsync(2026, "fall", ct));
        Assert.Equal(1, searches);
        Assert.Equal(2, seasonPages);
    }

    [Fact]
    public async Task AniList_GraphQlFailureIsNotPersistedAsMissingMedia()
    {
        var (factory, repo, _) = await CreateCacheAsync();
        int requests = 0;
        using var http = new HttpClient(new ScriptedHandler((_, _) => ++requests == 1
            ? Json("""{"errors":[{"message":"temporarily unavailable"}],"data":{"Media":null}}""")
            : Json("""{"data":{"Media":{"id":13,"idMal":12,"status":"RELEASING","episodes":24}}}""")));
        using var aniList = new AniListApiService(http, repo, Settings());
        var ct = CancellationToken.None;

        Assert.Null(await aniList.GetNextAiringAsync(12, ct: ct));
        await using var db = factory.CreateDbContext();
        Assert.Equal(0, await db.HttpResponseCache.CountAsync(ct));
        Assert.Equal(24, (await aniList.GetNextAiringAsync(12, ct: ct))?.TotalEpisodes);
        Assert.Equal(24, (await aniList.GetNextAiringAsync(12, ct: ct))?.TotalEpisodes);
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData("currently_airing", "tv", MediaKind.Anime, 1)]
    [InlineData("finished_airing", "tv", MediaKind.Anime, 14)]
    [InlineData("currently_publishing", "manga", MediaKind.Manga, 1)]
    [InlineData("finished", "manga", MediaKind.Manga, 14)]
    public async Task MalDetails_RevalidatesWhenStatusSpecificFreshnessExpires(string status, string type, MediaKind kind, int days)
    {
        var (factory, repo, clock) = await CreateCacheAsync();
        int requests = 0;
        using var http = new HttpClient(new ScriptedHandler((request, _) =>
        {
            if (++requests == 1)
                return Task.FromResult(Tagged(HttpStatusCode.OK,
                    $$"""{"id":42,"title":"Title","status":"{{status}}","media_type":"{{type}}"}""", "original"));

            Assert.Equal("W/\"original\"", Assert.Single(request.Headers.IfNoneMatch).ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        }));
        var settings = Settings();
        using var tokenManager = new MalTokenManager(settings, new MalAuthService(http));
        using var mal = new MalApiService(http, settings, tokenManager, repo, clock);

        Task<AnimeEntity?> Get() => kind == MediaKind.Anime ? mal.GetAnimeDetailsAsync(42) : mal.GetMangaDetailsAsync(42);

        Assert.Equal(kind, (await Get())?.MediaKind);
        clock.Advance(TimeSpan.FromDays(days) - TimeSpan.FromSeconds(1));
        Assert.True((await mal.ReadCachedDetailsAsync(42, kind)).IsFresh);
        Assert.Equal(kind, (await Get())?.MediaKind);
        Assert.Equal(1, requests);

        clock.Advance(TimeSpan.FromSeconds(1));
        var expired = await mal.ReadCachedDetailsAsync(42, kind);
        Assert.Equal("Title", expired.Details?.Title);
        Assert.False(expired.IsFresh);
        Assert.Equal(1, requests);
        Assert.Null(await mal.RefreshCachedDetailsAsync(42, kind));
        Assert.Equal(2, requests);
        Assert.True((await mal.ReadCachedDetailsAsync(42, kind)).IsFresh);
        await using var db = factory.CreateDbContext();
        Assert.Equal(clock.GetUtcNow().UtcDateTime, (await db.HttpResponseCache.SingleAsync()).CreatedAt);
    }

    [Fact]
    public async Task MalDetails_UnexpectedPayloadCannotReplaceLastGoodSnapshot()
    {
        var (factory, repo, clock) = await CreateCacheAsync();
        int requests = 0;
        using var http = new HttpClient(new ScriptedHandler((_, _) => ++requests == 1
            ? Json("""{"id":42,"title":"Last good snapshot","status":"currently_airing"}""")
            : Json("""{"id":99,"title":"Unexpected title"}""")));
        var settings = Settings();
        using var tokenManager = new MalTokenManager(settings, new MalAuthService(http));
        using var mal = new MalApiService(http, settings, tokenManager, repo, clock);
        var writtenAt = clock.GetUtcNow().UtcDateTime;
        Assert.NotNull(await mal.GetAnimeDetailsAsync(42));
        clock.Advance(TimeSpan.FromDays(2));

        Assert.Null(await mal.RefreshCachedDetailsAsync(42, MediaKind.Anime));
        var snapshot = await mal.ReadCachedDetailsAsync(42, MediaKind.Anime);
        Assert.Equal("Last good snapshot", snapshot.Details?.Title);
        Assert.False(snapshot.IsFresh);
        await using var db = factory.CreateDbContext();
        Assert.Equal(writtenAt, (await db.HttpResponseCache.SingleAsync()).CreatedAt);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task MalRecognition_RecoversAfterOutageWithoutSavingNegativeMatch()
    {
        var (factory, repo, _) = await CreateCacheAsync();
        int requests = 0;
        using var http = new HttpClient(new ScriptedHandler((_, _) => ++requests == 1
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
            : Json("""{"data":[{"node":{"id":42,"title":"Frieren"}}]}""")));
        var settings = Settings();
        using var tokenManager = new MalTokenManager(settings, new MalAuthService(http));
        using var mal = new MalApiService(http, settings, tokenManager, repo);
        var mapping = new MappingService(mal,
            new ManualMappingService(repository: null, legacyMappingFilePath: Path.Combine(_directory.FullName, "mapping.json")),
            new MalSearchCacheRepository(factory), new RecognitionCache());

        Assert.Null(await mapping.SearchOnMalAsync("Frieren"));
        await using var db = factory.CreateDbContext();
        Assert.Equal(0, await db.MalSearchCache.CountAsync());
        Assert.Equal(42, await mapping.SearchOnMalAsync("Frieren"));
        Assert.Equal(42, await mapping.SearchOnMalAsync("Frieren"));
        Assert.Equal(42, (await db.MalSearchCache.SingleAsync()).AnimeId);
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.OK)]
    public async Task EmptyOrMissingImage_DoesNotWriteFileOrRetry(HttpStatusCode status)
    {
        int requests = 0;
        using var http = new HttpClient(new ScriptedHandler((_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent([]) });
        }));
        var clientFactory = new Mock<IHttpClientFactory>();
        clientFactory.Setup(x => x.CreateClient("ImageClient")).Returns(http);
        using var downloader = new ImageDownloader(clientFactory.Object, _directory.FullName);
        Assert.Empty(await downloader.GetLocalPathOrDownload("https://image.test/poster.jpg", CancellationToken.None));
        Assert.Empty(_directory.GetFiles());
        Assert.Equal(1, requests);
    }

    private static Task<HttpCacheResult> SendAsync(HttpConditionalCache cache, CancellationToken ct = default) =>
        cache.SendForResultAsync(Url, _ => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, Url)), ct: ct, localTtl: Freshness);

    private async Task<(TestDbFactory, HttpCacheRepository, VirtualClock)> CreateCacheAsync()
    {
        var connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory.FullName, "cache.db"), Pooling = false };
        var factory = new TestDbFactory(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection.ToString()).Options);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(CancellationToken.None);
        var clock = new VirtualClock(DateTimeOffset.UtcNow);
        return (factory, new HttpCacheRepository(factory, clock), clock);
    }

    private static ISettingsService Settings()
    {
        var mock = new Mock<ISettingsService>();
        mock.SetupGet(x => x.Current).Returns(new AppSettings());
        return mock.Object;
    }

    private static HttpResponseMessage Tagged(HttpStatusCode status, string body, string tag)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        response.Headers.ETag = new EntityTagHeaderValue($"\"{tag}\"", isWeak: true);
        return response;
    }

    private static Task<HttpResponseMessage> Json(string json) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    public void Dispose() => _directory.Delete(recursive: true);

    private sealed class TestDbFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    private sealed class VirtualClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan interval) => now += interval;
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handle(request, cancellationToken);
    }
}
