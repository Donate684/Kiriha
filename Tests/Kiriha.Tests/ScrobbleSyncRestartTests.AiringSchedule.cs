using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Core.Tracking.Api;
using Kiriha.Data.Repository;
using Kiriha.Services.Maintenance;
using System.Text;
using System.Text.Json;
using Moq;

namespace Kiriha.Tests;

public sealed partial class ScrobbleSyncRestartTests
{
    [Fact]
    public async Task AniListMaintenance_CollectsStaggeredTitlesForNextHourlyPoll()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var start = new DateTimeOffset(2026, 11, 22, 20, 0, 0, TimeSpan.Zero);
        var clock = new AiringClock(start);
        foreach (var (offset, delay) in new[] { (0, 0), (1, 10), (2, 35) })
        {
            var title = new AnimeEntity
            {
                Id = AnimeId + offset, Title = $"Title {offset}", StatusDetailed = "currently_airing",
                NextEpisodeAt = start.AddMinutes(delay).UtcDateTime
            };
            Assert.True((await scenario.Actions.AddToListAsync(title, UserAnimeStatus.Watching, 0, ct)).Success);
            var tracked = Assert.IsType<AnimeEntity>(scenario.Library.FindById(title.Id));
            tracked.LastEpisodesSync = start.AddMinutes(-10).UtcDateTime;
            Assert.True(await scenario.Store.UpdateAiringAsync(tracked, ct));
        }

        var requests = new List<int[]>();
        var api = new Mock<IAniListApiService>();
        api.Setup(x => x.GetAiringSchedulesAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
            {
                requests.Add(ids.Order().ToArray());
                return new Dictionary<int, EpisodeAiringInfo>();
            });
        var service = new AiringInfoService(api.Object, Mock.Of<IShikiApiService>(), CreateMetadataSettings(),
            scenario.Library, scenario.Store, Mock.Of<IAnimeSyncOrchestrator>(), Mock.Of<INotificationService>(),
            new ImmediateProgressDispatcher(), syncGate: scenario.Gate, clock: clock);
        var task = new AiringSyncMaintenanceTask(service);

        await task.ExecuteAsync(ct);
        Assert.Equal([AnimeId], Assert.Single(requests));
        clock.Advance(TimeSpan.FromMinutes(10));
        await task.ExecuteAsync(ct);
        var manuallyChecked = Assert.IsType<AnimeEntity>(scenario.Library.FindById(AnimeId + 1));
        await service.SyncEpisodesForAnimeAsync(manuallyChecked, ct);
        api.Verify(x => x.GetNextAiringAsync(AnimeId + 1, true, ct), Times.Once);
        clock.Advance(TimeSpan.FromMinutes(49));
        await task.ExecuteAsync(ct);
        Assert.Single(requests);

        clock.Advance(TimeSpan.FromMinutes(1));
        await Task.WhenAll(task.ExecuteAsync(ct), task.ExecuteAsync(ct));
        Assert.Equal(2, requests.Count);
        Assert.Equal([AnimeId, AnimeId + 1, AnimeId + 2], requests[1]);
    }

    [Theory]
    [InlineData(-12)]
    [InlineData(24)]
    public async Task AniListMaintenance_BatchesRescheduleAndPreservesMissingTitle(int shiftHours)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var now = new DateTimeOffset(2026, 11, 22, 20, 0, 0, TimeSpan.Zero);
        var oldSlot = now.AddHours(36);
        var newSlot = oldSlot.AddHours(shiftHours);
        var clock = new AiringClock(now.AddMinutes(-1));
        var previousSync = now.AddHours(-12).UtcDateTime;
        var moved = await AddAsync(AnimeId);
        var omitted = await AddAsync(AnimeId + 1);
        int requests = 0;
        using var http = new HttpClient(new AiringBatchHandler(async request =>
        {
            var content = Assert.IsAssignableFrom<HttpContent>(request.Content);
            using var payload = JsonDocument.Parse(await content.ReadAsStringAsync());
            Assert.Contains("idMal_in", payload.RootElement.GetProperty("query").GetString());
            var ids = payload.RootElement.GetProperty("variables").GetProperty("malIds").EnumerateArray()
                .Select(item => item.GetInt32()).Order();
            Assert.Equal([moved.Id, omitted.Id], ids);
            requests++;
            return $$"""
                {"data":{"Page":{"media":[{"id":10,"idMal":{{moved.Id}},"status":"RELEASING",
                "episodes":12,"nextAiringEpisode":{"episode":5,"airingAt":{{newSlot.ToUnixTimeSeconds()}} } } ] } } }
                """;
        }));
        var settings = CreateMetadataSettings();
        using var api = new AniListApiService(http, new HttpCacheRepository(CreateFactory(), clock), settings, clock: clock);
        var notifications = new Mock<INotificationService>();
        var service = new AiringInfoService(api, Mock.Of<IShikiApiService>(), settings, scenario.Library, scenario.Store,
            Mock.Of<IAnimeSyncOrchestrator>(), notifications.Object, new ImmediateProgressDispatcher(),
            syncGate: scenario.Gate, clock: clock);
        var task = new AiringSyncMaintenanceTask(service);
        await task.ExecuteAsync(ct);
        Assert.Equal(0, requests);

        clock.Advance(TimeSpan.FromMinutes(1));
        await task.ExecuteAsync(ct);
        await task.ExecuteAsync(ct);
        Assert.Equal(1, requests);
        var updated = Assert.IsType<AnimeEntity>(await scenario.Store.GetByIdAsync(moved.Id, ct: ct));
        var kept = Assert.IsType<AnimeEntity>(await scenario.Store.GetByIdAsync(omitted.Id, ct: ct));
        Assert.Equal(4, updated.EpisodesAired);
        Assert.Equal(newSlot.UtcDateTime, updated.NextEpisodeAt);
        Assert.Equal(now.UtcDateTime, updated.LastEpisodesSync);
        Assert.Equal(oldSlot.UtcDateTime, kept.NextEpisodeAt);
        Assert.Equal(previousSync, kept.LastEpisodesSync);
        notifications.Verify(x => x.NotifyNewEpisode(It.IsAny<AnimeEntity>(), It.IsAny<int>()), Times.Never);
        Assert.Equal(newSlot.UtcDateTime, (await api.GetNextAiringAsync(moved.Id, ct: ct))?.NextEpisodeAt);
        Assert.Equal(1, requests);

        async Task<AnimeEntity> AddAsync(int id)
        {
            var anime = new AnimeEntity
            {
                Id = id, Title = $"Title {id}", TotalEpisodes = 12, EpisodesAired = 4,
                StatusDetailed = "currently_airing", NextEpisodeAt = oldSlot.UtcDateTime
            };
            Assert.True((await scenario.Actions.AddToListAsync(anime, UserAnimeStatus.Watching, 4, ct)).Success);
            var tracked = Assert.IsType<AnimeEntity>(scenario.Library.FindById(id));
            tracked.LastEpisodesSync = previousSync;
            Assert.True(await scenario.Store.UpdateAiringAsync(tracked, ct));
            return tracked;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AiringMaintenance_WaitsForReleaseAndNotifiesOnlyWhenProviderConfirms(bool shiki)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var release = new DateTimeOffset(2026, 11, 22, 20, 0, 0, TimeSpan.Zero);
        var clock = new AiringClock(release.AddHours(-6));
        var item = new AnimeEntity
        {
            Id = AnimeId, Title = "Ongoing", StatusDetailed = "currently_airing",
            EpisodesAired = 4, TotalEpisodes = 12, NextEpisodeAt = release.UtcDateTime
        };
        Assert.True((await scenario.Actions.AddToListAsync(item, UserAnimeStatus.Watching, 4, ct)).Success);
        var tracked = scenario.Library.FindById(AnimeId);
        Assert.NotNull(tracked);
        tracked.LastEpisodesSync = clock.GetUtcNow().UtcDateTime;
        Assert.True(await scenario.Store.UpdateAiringAsync(tracked, ct));
        int calls = 0;
        EpisodeAiringInfo? Fetch(bool forced)
        {
            Assert.True(forced);
            return ++calls switch
            {
                1 => new(10, AnimeId, "ongoing", 5, release.UtcDateTime, 12, shiki ? 4 : null),
                2 => new EpisodeAiringInfo(10, AnimeId, "ongoing", 6, release.AddDays(7).UtcDateTime, 12)
                    { IsStaleFallback = true },
                _ => new(10, AnimeId, "ongoing", 6, release.AddDays(7).UtcDateTime, 12, shiki ? 5 : null)
            };
        }

        var (task, notifications) = CreateMaintenance(scenario, clock, shiki, Fetch);
        await task.ExecuteAsync(ct);
        Assert.Equal(0, calls);

        clock.Advance(TimeSpan.FromHours(6));
        await Task.WhenAll(task.ExecuteAsync(ct), task.ExecuteAsync(ct));
        Assert.Equal(1, calls);
        var delayed = await scenario.Store.GetByIdAsync(AnimeId, ct: ct);
        Assert.NotNull(delayed);
        Assert.Equal((4, release.UtcDateTime, release.UtcDateTime),
            (delayed.EpisodesAired, delayed.NextEpisodeAt.GetValueOrDefault(), delayed.LastEpisodesSync.GetValueOrDefault()));

        var firstRetry = shiki ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(1);
        clock.Advance(firstRetry - TimeSpan.FromMinutes(1));
        await task.ExecuteAsync(ct);
        Assert.Equal(1, calls);
        clock.Advance(TimeSpan.FromMinutes(1));
        await task.ExecuteAsync(ct);
        Assert.Equal(2, calls);
        var failed = await scenario.Store.GetByIdAsync(AnimeId, ct: ct);
        Assert.NotNull(failed);
        Assert.Equal((delayed.EpisodesAired, delayed.NextEpisodeAt, delayed.LastEpisodesSync),
            (failed.EpisodesAired, failed.NextEpisodeAt, failed.LastEpisodesSync));
        notifications.Verify(x => x.NotifyNewEpisode(It.IsAny<AnimeEntity>(), It.IsAny<int>()), Times.Never);

        var secondRetry = shiki ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(6);
        clock.Advance(secondRetry - TimeSpan.FromMinutes(1));
        await task.ExecuteAsync(ct);
        Assert.Equal(2, calls);
        clock.Advance(TimeSpan.FromMinutes(1));
        await task.ExecuteAsync(ct);
        var confirmed = await scenario.Store.GetByIdAsync(AnimeId, ct: ct);
        Assert.NotNull(confirmed);
        Assert.Equal(5, confirmed.EpisodesAired);
        Assert.Equal(release.AddDays(7).UtcDateTime, confirmed.NextEpisodeAt);
        clock.Advance(TimeSpan.FromHours(6));
        await task.ExecuteAsync(ct);
        Assert.Equal(3, calls);
        notifications.Verify(x => x.NotifyNewEpisode(It.Is<AnimeEntity>(a => a.Id == AnimeId), 5), Times.Once);
    }

    [Theory]
    [InlineData(31, 60)]
    [InlineData(600, 119)]
    public async Task AniListMaintenance_RechecksUpcomingSlotOnlyAtRelevantBoundary(int minutesUntilRelease, int minutesSinceSync)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var now = new DateTimeOffset(2026, 11, 22, 20, 0, 0, TimeSpan.Zero);
        var slot = now.AddMinutes(minutesUntilRelease).UtcDateTime;
        var clock = new AiringClock(now);
        var anime = new AnimeEntity
        {
            Id = AnimeId, Title = "Ongoing", StatusDetailed = "currently_airing",
            EpisodesAired = 4, TotalEpisodes = 12, NextEpisodeAt = slot
        };
        Assert.True((await scenario.Actions.AddToListAsync(anime, UserAnimeStatus.Watching, 4, ct)).Success);
        var tracked = Assert.IsType<AnimeEntity>(scenario.Library.FindById(AnimeId));
        tracked.LastEpisodesSync = now.AddMinutes(-minutesSinceSync).UtcDateTime;
        Assert.True(await scenario.Store.UpdateAiringAsync(tracked, ct));

        int calls = 0;
        var (task, notifications) = CreateMaintenance(scenario, clock, false, _ =>
        {
            calls++;
            return new EpisodeAiringInfo(10, AnimeId, "RELEASING", 5, slot, 12);
        });
        await task.ExecuteAsync(ct);
        Assert.Equal(0, calls);
        clock.Advance(TimeSpan.FromMinutes(1));
        await task.ExecuteAsync(ct);
        clock.Advance(TimeSpan.FromMinutes(1));
        await task.ExecuteAsync(ct);
        Assert.Equal(1, calls);
        notifications.Verify(x => x.NotifyNewEpisode(It.IsAny<AnimeEntity>(), It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(false, 24)]
    [InlineData(true, 6)]
    public async Task AiringMaintenance_BacksOffForMissingOrLongOverdueScheduleAfterFailedRequest(bool overdue, int retryHours)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var clock = new AiringClock(new DateTimeOffset(2026, 11, 22, 20, 0, 0, TimeSpan.Zero));
        var item = new AnimeEntity
        {
            Id = AnimeId, Title = "Ongoing", StatusDetailed = "currently_airing",
            NextEpisodeAt = overdue ? clock.GetUtcNow().AddHours(-2).UtcDateTime : null
        };
        Assert.True((await scenario.Actions.AddToListAsync(item, UserAnimeStatus.Watching, 0, ct)).Success);
        int calls = 0;
        var (task, _) = CreateMaintenance(scenario, clock, true, forced =>
        {
            Assert.Equal(overdue, forced);
            calls++;
            return null;
        });
        await task.ExecuteAsync(ct);
        Assert.Equal(1, calls);
        clock.Advance(TimeSpan.FromHours(retryHours) - TimeSpan.FromMinutes(1));
        await task.ExecuteAsync(ct);
        Assert.Equal(1, calls);
        clock.Advance(TimeSpan.FromMinutes(1));
        await task.ExecuteAsync(ct);
        Assert.Equal(2, calls);
        Assert.Null((await scenario.Store.GetByIdAsync(AnimeId, ct: ct))?.LastEpisodesSync);
    }

    private static (AiringSyncMaintenanceTask Task, Mock<INotificationService> Notifications) CreateMaintenance(
        LibraryScenario scenario, TimeProvider clock, bool shiki, Func<bool, EpisodeAiringInfo?> fetch)
    {
        var aniList = new Mock<IAniListApiService>();
        var shikimori = new Mock<IShikiApiService>();
        if (shiki)
            shikimori.Setup(x => x.GetAiringInfoAsync(AnimeId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int _, bool force, CancellationToken _) => fetch(force));
        else
            aniList.Setup(x => x.GetAiringSchedulesAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                {
                    Assert.Equal(AnimeId, Assert.Single(ids));
                    return fetch(true) is { } airing
                        ? new Dictionary<int, EpisodeAiringInfo> { [AnimeId] = airing }
                        : new Dictionary<int, EpisodeAiringInfo>();
                });

        var settings = new AppSettings();
        settings.System.AiringSource = shiki ? EpisodeAiringSource.Shikimori : EpisodeAiringSource.AniList;
        var notifications = new Mock<INotificationService>();
        var service = new AiringInfoService(aniList.Object, shikimori.Object,
            Mock.Of<ISettingsService>(s => s.Current == settings), scenario.Library, scenario.Store,
            Mock.Of<IAnimeSyncOrchestrator>(), notifications.Object, new ImmediateProgressDispatcher(),
            syncGate: scenario.Gate, clock: clock);
        return (new AiringSyncMaintenanceTask(service), notifications);
    }

    private sealed class AiringClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan interval) => now += interval;
    }

    private sealed class AiringBatchHandler(Func<HttpRequestMessage, Task<string>> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            new(System.Net.HttpStatusCode.OK) { Content = new StringContent(await respond(request), Encoding.UTF8, "application/json") };
    }
}
