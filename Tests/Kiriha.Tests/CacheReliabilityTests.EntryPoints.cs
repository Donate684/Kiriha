using System.Net;
using Kiriha.Core.Abstractions.Infrastructure;
using Kiriha.Core.Abstractions.Repositories;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Dialogs;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Api;
using Kiriha.Core.Tracking.Auth;
using Kiriha.Core.Tracking.Feed;
using Kiriha.Infrastructure.Caching;
using Kiriha.ViewModels.Seasonal;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kiriha.Tests;

public sealed partial class CacheReliabilityTests
{
    [Fact]
    public async Task SeasonalNavigation_UsesDiskThenRevalidatesAndDisplaysNewDetailsForSameId()
    {
        var (_, repo, clock) = await CreateCacheAsync();
        var instant = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        clock.Advance(instant - clock.GetUtcNow());
        var store = new SeasonalCacheStore(_directory.FullName, clock);
        await store.SaveAsync(2026, "fall",
            [new AnimeEntity { Id = 42, Title = "Saved", Type = "TV", StartYear = 2026, StartSeason = "fall" }], validated: true);
        int requests = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var http = new HttpClient(new ScriptedHandler(async (request, ct) =>
        {
            Assert.Contains("/anime/season/2026/fall", request.RequestUri?.AbsolutePath);
            if (Interlocked.Increment(ref requests) != 1)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            started.SetResult();
            await release.Task.WaitAsync(ct);
            return await Json("""{"data":[{"node":{"id":42,"title":"Updated","media_type":"tv","start_season":{"year":2026,"season":"fall"}}}]}""");
        }));
        var settings = Settings();
        using var tokens = new MalTokenManager(settings, new MalAuthService(http));
        using var mal = new MalApiService(http, settings, tokens, repo, clock);
        using var vm = CreateSeasonalViewModel(mal, store, clock);

        try
        {
            await vm.OnOpenedAsync().WaitAsync(timeout.Token);
            await vm.OnOpenedAsync().WaitAsync(timeout.Token);
            Assert.Equal("Saved", Assert.Single(vm.DisplayItems).Title);
            Assert.Equal(0, requests);

            clock.Advance(TimeSpan.FromHours(3));
            var reopen = vm.OnOpenedAsync();
            await started.Task.WaitAsync(timeout.Token);
            Assert.Equal("Saved", Assert.Single(vm.DisplayItems).Title);
            Assert.False(vm.IsLoading);
            release.SetResult();
            await reopen.WaitAsync(timeout.Token);

            Assert.Equal("Updated", Assert.Single(vm.DisplayItems).Title);
            await vm.OnOpenedAsync().WaitAsync(timeout.Token);
            Assert.Equal(1, requests);

            await vm.ForceRefreshAsync().WaitAsync(timeout.Token);
            Assert.Equal(2, requests);
            Assert.Equal("Updated", Assert.Single(vm.DisplayItems).Title);
            Assert.Equal("Updated", Assert.Single(Assert.Single(store.LoadAll()).Items).Title);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task NyaaRssSearch_UsesFreshResultsAndKeepsThemOnUpstreamFailure()
    {
        var (factory, repo, clock) = await CreateCacheAsync();
        int requests = 0;
        using var http = new HttpClient(new ScriptedHandler((request, _) =>
        {
            if (++requests == 1)
                return Task.FromResult(Tagged(HttpStatusCode.OK,
                    """<rss><channel><item><title>Frieren - 03</title></item></channel></rss>""", "rss"));

            Assert.Equal("W/\"rss\"", Assert.Single(request.Headers.IfNoneMatch).ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }));
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(x => x.CreateClient("RssClient")).Returns(http);
        var library = new Mock<IAnimeRepository>();
        library.Setup(x => x.GetCollection()).Returns([]);
        var rss = new RssFeedService(new NyaaFeedClient(clients.Object, repo, clock), library.Object, new InlineDispatcher());

        var storedAt = clock.GetUtcNow().UtcDateTime;
        Assert.Equal("Frieren - 03", Assert.Single(await rss.SearchTorrentsAsync("Frieren")).Title);
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Single(await rss.SearchTorrentsAsync("Frieren"));
        Assert.Equal(1, requests);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(await rss.SearchTorrentsAsync("Frieren"));
        Assert.Equal(2, requests);
        await using var db = factory.CreateDbContext();
        Assert.Equal(storedAt, (await db.HttpResponseCache.SingleAsync()).CreatedAt);
    }

    [Fact]
    public async Task AniListAiring_UsesSixHourCacheAndManualCheckCanBypassIt()
    {
        var (factory, repo, clock) = await CreateCacheAsync();
        int requests = 0;
        using var http = new HttpClient(new ScriptedHandler((_, _) => ++requests == 1
            ? Json("""{"data":{"Media":{"id":9,"idMal":42,"status":"RELEASING","episodes":12}}}""")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        using var aniList = new AniListApiService(http, repo, Settings(), clock: clock);
        var storedAt = clock.GetUtcNow().UtcDateTime;

        Assert.Equal(12, (await aniList.GetNextAiringAsync(42))?.TotalEpisodes);
        clock.Advance(TimeSpan.FromHours(6));
        Assert.Equal(12, (await aniList.GetNextAiringAsync(42))?.TotalEpisodes);
        Assert.Equal(1, requests);
        Assert.True((await aniList.GetNextAiringAsync(42, force: true))?.IsStaleFallback);
        Assert.Equal(2, requests);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True((await aniList.GetNextAiringAsync(42))?.IsStaleFallback);
        Assert.Equal(3, requests);
        await using var db = factory.CreateDbContext();
        Assert.Equal(storedAt, (await db.HttpResponseCache.SingleAsync()).CreatedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShikiReads_UseTheirCacheAndRevalidateOnExpiry(bool franchise)
    {
        var (_, repo, clock) = await CreateCacheAsync();
        var body = franchise
            ? """{"current_id":42,"nodes":[{"id":42,"name":"Title"}],"links":[]}"""
            : """{"id":42,"status":"ongoing","episodes":12,"episodes_aired":4}""";
        int requests = 0;
        using var http = new HttpClient(new ScriptedHandler((request, _) =>
        {
            if (++requests == 1)
                return Task.FromResult(Tagged(HttpStatusCode.OK, body, "shiki"));

            Assert.Equal("W/\"shiki\"", Assert.Single(request.Headers.IfNoneMatch).ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        }));
        var settings = Settings();
        var resolver = new ShikiHostResolver();
        var tokens = new ShikiTokenService(settings, new ShikiAuthService(http, settings, resolver));
        using var limiter = new ShikiRateLimiter();
        var shiki = new ShikiApiService(http, settings, tokens, resolver, repo, limiter, fixedMirror: ShikiMirror.Net, clock: clock);
        async Task<int?> ReadAsync() => franchise
            ? (await shiki.GetFranchiseAsync(42))?.CurrentId
            : (await shiki.GetAiringInfoAsync(42))?.MalId;

        Assert.Equal(42, await ReadAsync());
        clock.Advance((franchise ? TimeSpan.FromDays(30) : TimeSpan.FromHours(6)) - TimeSpan.FromSeconds(1));
        Assert.Equal(42, await ReadAsync());
        Assert.Equal(1, requests);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(42, await ReadAsync());
        Assert.Equal(42, await ReadAsync());
        Assert.Equal(2, requests);
    }

    private static SeasonalViewModel CreateSeasonalViewModel(IMalApiService mal, SeasonalCacheStore store, TimeProvider clock)
    {
        var hidden = new Mock<ISeasonalHiddenRepository>();
        HashSet<int> hiddenIds = [];
        hidden.Setup(x => x.GetHiddenIds()).Returns(hiddenIds);
        var franchise = new Mock<IFranchiseService>();
        franchise.Setup(x => x.GetIndex()).Returns(new Dictionary<int, FranchiseContext>());
        return new SeasonalViewModel(mal, Settings(), Mock.Of<ILoadQueueService>(), Mock.Of<IAnimeRepository>(), store,
            Mock.Of<IAnimeListActionService>(), Mock.Of<IDialogService>(), ILocalizer.Passthrough, franchise.Object,
            Mock.Of<IMetadataRepository>(), hidden.Object, Mock.Of<IAnimeCountryService>(), new InlineDispatcher(), clock);
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());

        public Task InvokeAsync(Func<Task> action) => action();
    }
}
