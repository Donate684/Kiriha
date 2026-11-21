using System.Threading.Channels;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Abstractions.Services.AppLifecycle;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Core.Tracking.Core;
using Kiriha.Data.Repository;
using Moq;

namespace Kiriha.Tests;

public sealed partial class ScrobbleSyncRestartTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ActiveCountdown_RejectsOldEntryAfterRemovalAndReAdd(bool reAdd, bool bypassRefresh)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var item = await AddCountdownAnimeAsync(scenario, ct);
        var store = new SqliteScrobbleProgressStore(CreateFactory());
        await using var playback = new CountdownPlayback(scenario, store, delaySeconds: 8);
        var state = PlayerState(item);
        if (bypassRefresh)
        {
            playback.Scrobbler.StartScrobble(new ParsedMedia { Episode = "3", IsPlaying = true }, item);
        }
        else
        {
            await playback.Tracking.SetInternalMediaAsync(state);
        }

        var fire = await playback.Clock.NextTickAsync(ct);
        playback.Clock.Advance(TimeSpan.FromSeconds(6));
        fire();
        var oldTick = await playback.Clock.NextTickAsync(ct);
        var oldCountdown = Assert.Single(playback.Countdowns);

        if (bypassRefresh)
        {
            // Emulate a removal that hasn't delivered a UI refresh.
            await scenario.Library.RemoveAnimeLocalAsync(item.Id, item.MediaKind);
        }
        else
        {
            Assert.True((await scenario.Actions.RemoveFromListAsync(item.Id, ct)).Success);
            Assert.Equal(UserAnimeStatus.None, playback.Tracking.MatchedAnime?.Status);
        }

        if (reAdd)
        {
            // Reuse the same instance, so the generation must distinguish the replacement.
            Assert.True((await scenario.Actions.AddToListAsync(item, UserAnimeStatus.Watching, 2, ct)).Success);
            Assert.Same(item, playback.Tracking.MatchedAnime);
        }

        playback.Clock.Advance(TimeSpan.FromSeconds(2));
        oldTick();
        await oldCountdown.WaitAsync(ct);

        Assert.Equal(reAdd ? 2 : (int?)null, (await scenario.Store.GetByIdAsync(item.Id, ct: ct))?.Progress);
        var expectedActions = 1 + (bypassRefresh ? 0 : 1) + (reAdd ? 1 : 0);
        await AssertLibraryActionsAsync(scenario, expectedActions, scrobbles: 0, ct);
        if (!reAdd) return;

        // The replacement must watch the full delay, including the time already advanced since re-adding.
        var newTick = await playback.Clock.NextTickAsync(ct);
        playback.Clock.Advance(TimeSpan.FromSeconds(5));
        newTick();
        newTick = await playback.Clock.NextTickAsync(ct);
        Assert.Equal(2, item.Progress);
        playback.Clock.Advance(TimeSpan.FromSeconds(1));
        newTick();
        await playback.Countdowns[^1].WaitAsync(ct);
        Assert.Equal(3, (await scenario.Store.GetByIdAsync(item.Id, ct: ct))?.Progress);
        await AssertLibraryActionsAsync(scenario, expectedActions + 1, scrobbles: 1, ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveCountdown_WhenSameEpisodeChangesFile_StartsNewCountdown(bool external)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var item = await AddCountdownAnimeAsync(scenario, ct);
        var store = new SqliteScrobbleProgressStore(CreateFactory());
        await using var playback = new CountdownPlayback(scenario, store, delaySeconds: 3);
        var first = PlayerState(item);
        var second = PlayerState(item, secondSource: true);

        await SendAsync(first);
        var oldTick = await playback.Clock.NextTickAsync(ct);
        playback.Clock.Advance(TimeSpan.FromSeconds(2));
        oldTick();
        oldTick = await playback.Clock.NextTickAsync(ct);
        var oldCountdown = Assert.Single(playback.Countdowns);
        await SendAsync(second);
        await oldCountdown.WaitAsync(ct);
        Assert.Equal(2, playback.Countdowns.Count);

        var tick = await playback.Clock.NextTickAsync(ct);
        playback.Clock.Advance(TimeSpan.FromSeconds(1));
        oldTick();
        tick();
        tick = await playback.Clock.NextTickAsync(ct);
        Assert.Equal(2, (await scenario.Store.GetByIdAsync(item.Id, ct: ct))?.Progress);
        playback.Clock.Advance(TimeSpan.FromSeconds(2));
        tick();
        await playback.Countdowns[^1].WaitAsync(ct);

        Assert.Equal(3, (await scenario.Store.GetByIdAsync(item.Id, ct: ct))?.Progress);
        await AssertLibraryActionsAsync(scenario, actions: 2, scrobbles: 1, ct);

        Task SendAsync(InternalPlayerState state) => external
            ? playback.Tracking.ProcessDetectedMediaAsync(new ParsedMedia
            {
                OriginalTitle = state.OriginalTitle, AnimeTitle = state.AnimeTitle, Episode = state.Episode,
                ProcessName = "mpv", Pid = 17, PlaybackSourceKey = state.PlaybackSourceKey, IsPlaying = state.IsPlaying
            })
            : playback.Tracking.SetInternalMediaAsync(state);
    }

    [Fact]
    public async Task ActiveCountdown_PauseBufferingAndRepeatedEvents_OnlyCountPlayingTime()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var item = await AddCountdownAnimeAsync(scenario, ct);
        await using var playback = new CountdownPlayback(scenario, new SqliteScrobbleProgressStore(CreateFactory()), delaySeconds: 2);
        var state = PlayerState(item);
        await playback.Tracking.SetInternalMediaAsync(state);
        var fire = await playback.Clock.NextTickAsync(ct);
        playback.Clock.Advance(TimeSpan.FromMilliseconds(250));

        state.IsBuffering = true;
        await playback.Tracking.SetInternalMediaAsync(state);
        await playback.Tracking.SetInternalMediaAsync(state);
        playback.Clock.Advance(TimeSpan.FromSeconds(12));
        fire();
        fire = await playback.Clock.NextTickAsync(ct);
        Assert.Equal(2, item.Progress);

        state.IsBuffering = false;
        await playback.Tracking.SetInternalMediaAsync(state);
        playback.Clock.Advance(TimeSpan.FromMilliseconds(500));
        state.IsPlaying = false;
        await playback.Tracking.SetInternalMediaAsync(state);
        await playback.Tracking.SetInternalMediaAsync(state);
        playback.Clock.Advance(TimeSpan.FromSeconds(5));
        state.IsPlaying = true;
        state.Position = 600;
        await playback.Tracking.SetInternalMediaAsync(state);
        await playback.Tracking.SetInternalMediaAsync(state);
        playback.Clock.Advance(TimeSpan.FromSeconds(1));
        fire();
        fire = await playback.Clock.NextTickAsync(ct);
        Assert.Equal(2, item.Progress);

        playback.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fire();
        await Assert.Single(playback.Countdowns).WaitAsync(ct);
        Assert.Equal(3, (await scenario.Store.GetByIdAsync(item.Id, ct: ct))?.Progress);
        await AssertLibraryActionsAsync(scenario, actions: 2, scrobbles: 1, ct);
    }

    [Fact]
    public async Task ActiveCountdown_ResumesSameFileWithNewRequestFromSavedCheckpoint()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var item = await AddCountdownAnimeAsync(scenario, ct);
        var initial = new SqliteScrobbleProgressStore(CreateFactory());
        var state = PlayerState(item);
        await using (var playback = new CountdownPlayback(scenario, initial, delaySeconds: 3))
        {
            await playback.Tracking.SetInternalMediaAsync(state);
            _ = await playback.Clock.NextTickAsync(ct);
            playback.Clock.Advance(TimeSpan.FromSeconds(2));
            await playback.Tracking.SetInternalMediaAsync(new InternalPlayerState { IsClosed = true });
            await Assert.Single(playback.Countdowns).WaitAsync(ct);
        }
        Assert.Equal(2, item.Progress);

        await using var reopened = new CountdownPlayback(scenario, new SqliteScrobbleProgressStore(CreateFactory()), delaySeconds: 3);
        state.PlaybackRequestId = "reopened";
        await reopened.Tracking.SetInternalMediaAsync(state);
        var tick = await reopened.Clock.NextTickAsync(ct);
        reopened.Clock.Advance(TimeSpan.FromSeconds(1));
        tick();
        await Assert.Single(reopened.Countdowns).WaitAsync(ct);
        Assert.Equal(3, (await scenario.Store.GetByIdAsync(item.Id, ct: ct))?.Progress);
        await AssertLibraryActionsAsync(scenario, actions: 2, scrobbles: 1, ct);
    }

    [Fact]
    public async Task ActiveCountdown_DoesNotReuseClosedFilesCheckpointAfterReAddingTitle()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var item = await AddCountdownAnimeAsync(scenario, ct);
        var store = new SqliteScrobbleProgressStore(CreateFactory());
        await using var playback = new CountdownPlayback(scenario, store, delaySeconds: 3);
        var state = PlayerState(item);

        await playback.Tracking.SetInternalMediaAsync(state);
        _ = await playback.Clock.NextTickAsync(ct);
        playback.Clock.Advance(TimeSpan.FromSeconds(2));
        await playback.Tracking.SetInternalMediaAsync(new InternalPlayerState { IsClosed = true });
        await Assert.Single(playback.Countdowns).WaitAsync(ct);

        Assert.True((await scenario.Actions.RemoveFromListAsync(item.Id, ct)).Success);
        Assert.True((await scenario.Actions.AddToListAsync(item, UserAnimeStatus.Watching, 2, ct)).Success);
        state.PlaybackRequestId = "reopened";
        await playback.Tracking.SetInternalMediaAsync(state);

        var tick = await playback.Clock.NextTickAsync(ct);
        playback.Clock.Advance(TimeSpan.FromSeconds(1));
        tick();
        tick = await playback.Clock.NextTickAsync(ct);
        Assert.Equal(2, item.Progress);

        playback.Clock.Advance(TimeSpan.FromSeconds(2));
        tick();
        await playback.Countdowns[^1].WaitAsync(ct);
        Assert.Equal(3, (await scenario.Store.GetByIdAsync(item.Id, ct: ct))?.Progress);
        await AssertLibraryActionsAsync(scenario, actions: 4, scrobbles: 1, ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScrobbleWrite_RechecksMembershipAndSqliteAfterWaitingForWriteGate(bool databaseOnly)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var item = await AddCountdownAnimeAsync(scenario, ct);
        var key = new MediaKey(item.Id, item.MediaKind);
        var generation = scenario.Library.GetEntryGeneration(key.Id, key.Kind);
        Task<ScrobbleResult> pending;
        using (await scenario.Gate.AcquireProgressWriteAsync(ct))
        {
            pending = scenario.Progress.RecordScrobbleAsync(key, generation, 3, ct);
            Assert.False(pending.IsCompleted);
            if (databaseOnly)
            {
                await scenario.Store.DeleteAsync(key.Id, key.Kind, ct);
            }
            else
            {
                await scenario.Library.RemoveAnimeLocalAsync(key.Id, key.Kind);
                await scenario.Library.AddOrUpdateAnimeAsync(item);
            }
        }

        Assert.Equal(ScrobbleOutcome.EntryChanged, (await pending.WaitAsync(ct)).Outcome);
        Assert.Equal(databaseOnly ? null : 2, (await scenario.Store.GetByIdAsync(key.Id, key.Kind, ct))?.Progress);
        await AssertLibraryActionsAsync(scenario, actions: 1, scrobbles: 0, ct);
    }

    private static async Task<AnimeEntity> AddCountdownAnimeAsync(LibraryScenario scenario, CancellationToken ct)
    {
        var item = new AnimeEntity
        {
            Id = AnimeId, Title = "Test title", TotalEpisodes = 12, StatusDetailed = "currently_airing", Genres = ["Drama"]
        };
        Assert.True((await scenario.Actions.AddToListAsync(item, UserAnimeStatus.Watching, 2, ct)).Success);
        return item;
    }

    private static InternalPlayerState PlayerState(AnimeEntity anime, bool secondSource = false) => new()
    {
        AnimeId = anime.Id, AnimeTitle = anime.Title, Episode = "3", OriginalTitle = "same-episode.mkv", IsPlaying = true,
        PlaybackRequestId = secondSource ? "second" : "first", PlaybackSourceKey = secondSource ? "second-file" : "first-file"
    };

    private static async Task AssertLibraryActionsAsync(
        LibraryScenario scenario, int actions, int scrobbles, CancellationToken ct)
    {
        var history = await scenario.History.GetHistoryAsync(ct: ct);
        Assert.Equal(actions, history.Count);
        Assert.Equal(scrobbles, history.Count(row => row.ActionType == (int)HistoryActionType.Scrobbled));
        Assert.Equal(actions, (await scenario.Queue.GetPendingAsync(ct)).Count);
    }

    private sealed class CountdownPlayback : IAsyncDisposable
    {
        private readonly IScrobbleProgressStore _store;

        public PlaybackClock Clock { get; } = new();
        public List<Task> Countdowns { get; } = [];
        public ScrobbleService Scrobbler { get; }
        public TrackingService Tracking { get; }

        public CountdownPlayback(LibraryScenario scenario, IScrobbleProgressStore store, int delaySeconds,
            IInternalPlayerServer? playerServer = null)
        {
            _store = store;
            var config = new AppSettings();
            config.System.Scrobbler.Enabled = true;
            config.System.Scrobbler.Mode = ScrobbleDelayMode.Time;
            config.System.Scrobbler.DelaySeconds = delaySeconds;
            var settings = Mock.Of<ISettingsService>(settings => settings.Current == config);
            var dispatcher = new ImmediateProgressDispatcher();
            var background = new Mock<IBackgroundTaskSupervisor>();
            background.Setup(x => x.Run("ScrobbleService.Countdown", It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Func<CancellationToken, Task> work, CancellationToken ct) =>
                {
                    var task = work(ct);
                    Countdowns.Add(task);
                    return task;
                });
            var mapping = new Mock<IMappingService>();
            mapping.Setup(m => m.GetIdFromTitleAsync(It.IsAny<string>(), It.IsAny<IEnumerable<AnimeEntity>>())).ReturnsAsync(AnimeId);
            Scrobbler = new ScrobbleService(scenario.Progress, settings, Mock.Of<INotificationService>(),
                background.Object, dispatcher, ILocalizer.Passthrough, store, eventBus: scenario.Events, library: scenario.Library, clock: Clock);
            Tracking = new TrackingService(Mock.Of<IExternalMediaDetector>(), playerServer ?? Mock.Of<IInternalPlayerServer>(),
                mapping.Object, scenario.Library, settings, Mock.Of<IDiscordService>(), Scrobbler, dispatcher, [],
                new MediaMatchingPipeline(mapping.Object, []), eventBus: scenario.Events);
        }

        public async ValueTask DisposeAsync()
        {
            Tracking.Dispose();
            Scrobbler.Dispose();
            using var timeout = new CancellationTokenSource(Timeout);
            await Task.WhenAll(Countdowns).WaitAsync(timeout.Token);
            await _store.FlushAsync(timeout.Token);
        }
    }

    private sealed class PlaybackClock : TimeProvider
    {
        private readonly Channel<Action> _timers = Channel.CreateUnbounded<Action>();
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public void Advance(TimeSpan time) => Interlocked.Add(ref _timestamp, time.Ticks);
        public ValueTask<Action> NextTickAsync(CancellationToken ct) => _timers.Reader.ReadAsync(ct);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _timers.Writer.TryWrite(() => callback(state));
            return Mock.Of<ITimer>();
        }
    }
}
