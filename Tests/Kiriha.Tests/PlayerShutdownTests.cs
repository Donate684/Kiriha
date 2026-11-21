using System.Collections.Concurrent;
using System.IO.Pipes;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Infrastructure.Player;
using Kiriha.Mpv.UI.Services.Player;
using Kiriha.Services;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.AppLifecycle.Shutdown;
using Moq;

namespace Kiriha.Tests;

public sealed class PlayerShutdownTests : IDisposable
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(12);
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("Kiriha.PlayerShutdown.");

    [Fact]
    public async Task TrackerWaitsForAllPlayerWindowsAndDurableStateBeforeStoppingItsTorrServer()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        var pipe = NewPipeName();
        var windowsClosing = NewSignal();
        var firstWindow = NewResult();
        var secondWindow = NewResult();
        var flushStarted = NewSignal();
        var allowFlush = NewSignal();
        var releaseStarted = NewSignal();
        var allowRelease = NewSignal();
        var finished = NewSignal();
        var shutdownOrder = new ConcurrentQueue<string>();
        var positionPath = Path.Combine(_directory.FullName, "resume.json");
        const string media = "https://example.test/episode";
        var store = new FilePlaybackResumeStore(positionPath);

        var resume = new Mock<IPlaybackResumeStore>();
        resume.Setup(service => service.FlushAsync(It.IsAny<CancellationToken>())).Returns(async (CancellationToken token) =>
        {
            flushStarted.TrySetResult();
            await allowFlush.Task.WaitAsync(token);
            store.SavePosition(media, 42, 120);
            await store.FlushAsync(token);
            shutdownOrder.Enqueue("resume persisted");
        });
        var playerTorrServer = new Mock<ITorrServerService>();
        playerTorrServer.Setup(service => service.FlushTorrentReleasesAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken token) =>
            {
                releaseStarted.TrySetResult();
                await allowRelease.Task.WaitAsync(token);
                shutdownOrder.Enqueue("torrents released");
            });
        playerTorrServer.Setup(service => service.Stop()).Callback(() => shutdownOrder.Enqueue("player server stopped"));
        var trackerTorrServer = new Mock<ITorrServerService>();
        trackerTorrServer.Setup(service => service.FlushTorrentReleasesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        trackerTorrServer.Setup(service => service.Stop()).Callback(() => shutdownOrder.Enqueue("tracker server stopped"));
        var services = Mock.Of<IServiceProvider>();
        var playerDrain = new ShutdownCoordinator(services,
        [
            new ManualMappingShutdownHandler(playbackResumeStore: resume.Object),
            new TorrServerShutdownHandler(playerTorrServer.Object)
        ]);

        var closeCount = 0;
        var sequence = new PlayerShutdownSequence(() =>
        {
            Interlocked.Increment(ref closeCount);
            windowsClosing.TrySetResult();
            return [firstWindow.Task, secondWindow.Task];
        }, playerDrain.DrainAsync);
        await using var server = new PlayerCommandServer(_ => throw new InvalidOperationException("Unexpected player command"),
            pipe, Deadline, sequence.PrepareAsync, () => finished.TrySetResult());
        server.Start();

        PlayerShutdownResult? response = null;
        var request = new PlayerResidentShutdownHandler(async () =>
        {
            response = await RequestAsync(pipe, ct);
            return response.Value;
        });
        var trackerDrain = new ShutdownCoordinator(services, [request, new TorrServerShutdownHandler(trackerTorrServer.Object)]);
        var shutdown = trackerDrain.DrainAsync();
        try
        {
            await windowsClosing.Task.WaitAsync(ct);
            Assert.Same(sequence.PrepareAsync(), sequence.PrepareAsync());
            firstWindow.TrySetResult(true);
            Assert.False(flushStarted.Task.IsCompleted);
            Assert.False(shutdown.IsCompleted);
            secondWindow.TrySetResult(true);
            await flushStarted.Task.WaitAsync(ct);
            Assert.Null(new FilePlaybackResumeStore(positionPath).TryGetResumePosition(media));
            Assert.False(shutdown.IsCompleted);
            allowFlush.TrySetResult();
            await releaseStarted.Task.WaitAsync(ct);
            Assert.Equal(42, new FilePlaybackResumeStore(positionPath).TryGetResumePosition(media));
            Assert.False(shutdown.IsCompleted);
            trackerTorrServer.Verify(service => service.Stop(), Times.Never);
            allowRelease.TrySetResult();

            Assert.True(await shutdown.WaitAsync(ct));
            await finished.Task.WaitAsync(ct);
            Assert.Equal(PlayerShutdownResult.Completed, response);
            Assert.Equal(1, closeCount);
            Assert.Equal(["resume persisted", "torrents released", "player server stopped", "tracker server stopped"], shutdownOrder);
            Assert.True(await trackerDrain.DrainAsync());
            trackerTorrServer.Verify(service => service.Stop(), Times.Once);
        }
        finally
        {
            firstWindow.TrySetResult(true);
            secondWindow.TrySetResult(true);
            allowFlush.TrySetResult();
            allowRelease.TrySetResult();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CleanupFailureIsConfirmedAsFailureAndLaterHandlersStillRun(bool windowTimesOut)
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        var pendingWindow = NewResult();
        var pipe = NewPipeName();
        var resume = new Mock<IPlaybackResumeStore>();
        resume.Setup(store => store.FlushAsync(It.IsAny<CancellationToken>()))
            .Returns(windowTimesOut ? Task.CompletedTask : Task.FromException(new IOException("resume write failed")));
        var torrServer = new Mock<ITorrServerService>();
        torrServer.Setup(service => service.FlushTorrentReleasesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var drain = new ShutdownCoordinator(Mock.Of<IServiceProvider>(),
            [new ManualMappingShutdownHandler(playbackResumeStore: resume.Object), new TorrServerShutdownHandler(torrServer.Object)]);
        var closeTask = windowTimesOut ? pendingWindow.Task : Task.FromResult(true);
        var sequence = new PlayerShutdownSequence(() => [closeTask], drain.DrainAsync, TimeSpan.FromMilliseconds(100));
        var finished = NewSignal();
        await using var server = new PlayerCommandServer(_ => { }, pipe, Deadline, sequence.PrepareAsync, () => finished.TrySetResult());
        server.Start();

        Assert.Equal(PlayerShutdownResult.Failed, await RequestAsync(pipe, ct));
        await finished.Task.WaitAsync(ct);
        torrServer.Verify(service => service.Stop(), Times.Once);
        pendingWindow.TrySetResult(true);
    }

    [Fact]
    public async Task LateConfirmationDoesNotHoldTheTrackerOrCancelPlayerCleanup()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        var pipe = NewPipeName();
        var prepareStarted = NewSignal();
        var allowCleanup = NewResult();
        var finished = NewSignal();
        await using var server = new PlayerCommandServer(_ => { }, pipe, Deadline, async () =>
        {
            prepareStarted.TrySetResult();
            return await allowCleanup.Task;
        }, () => finished.TrySetResult());
        server.Start();

        var trackerServer = new Mock<ITorrServerService>();
        trackerServer.Setup(service => service.FlushTorrentReleasesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        PlayerShutdownResult? response = null;
        var trackerDrain = new ShutdownCoordinator(Mock.Of<IServiceProvider>(),
        [
            new PlayerResidentShutdownHandler(async () =>
            {
                response = await PlayerProcessBridge.SendShutdownCommandAsync(pipe, Deadline, TimeSpan.FromMilliseconds(500), ct);
                return response.Value;
            }),
            new TorrServerShutdownHandler(trackerServer.Object)
        ]);
        var shutdown = trackerDrain.DrainAsync();
        try
        {
            await prepareStarted.Task.WaitAsync(ct);
            Assert.False(await shutdown.WaitAsync(ct));
            Assert.Equal(PlayerShutdownResult.Unconfirmed, response);
            Assert.False(finished.Task.IsCompleted);
            trackerServer.Verify(service => service.Stop(), Times.Once);
        }
        finally
        {
            allowCleanup.TrySetResult(true);
        }
        await finished.Task.WaitAsync(ct);
    }

    [Fact]
    public async Task MissingPlayerReturnsUnavailableWithinConnectionBudget()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var result = await PlayerProcessBridge.SendShutdownCommandAsync(
            NewPipeName(), TimeSpan.FromMilliseconds(150), Deadline, timeout.Token);
        Assert.Equal(PlayerShutdownResult.Unavailable, result);
    }

    [Fact]
    public async Task LegacyWriteOnlyClientStillShutsDownTheNewPlayer()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        var pipe = NewPipeName();
        var finished = NewSignal();
        var closeCount = 0;
        await using var server = new PlayerCommandServer(_ => { }, pipe, Deadline, () =>
        {
            Interlocked.Increment(ref closeCount);
            return Task.FromResult(true);
        }, () => finished.TrySetResult());
        server.Start();

        Assert.True(await PlayerProcessBridge.SendCommandAsync(pipe, ["--player", PlayerProcessBridge.ShutdownArg], Deadline, ct));
        await finished.Task.WaitAsync(ct);
        Assert.Equal(1, closeCount);
    }

    [Fact]
    public async Task OldResidentWithoutAcknowledgementReceivesLegacyShutdown()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        var pipe = NewPipeName();
        using var legacy = new NamedPipeServerStream(pipe, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var listen = ReadLegacyAsync();
        var response = await RequestAsync(pipe, ct);

        Assert.Equal(PlayerShutdownResult.Unconfirmed, response);
        Assert.Contains(PlayerProcessBridge.ShutdownArg, await listen.WaitAsync(ct));

        async Task<string[]> ReadLegacyAsync()
        {
            await legacy.WaitForConnectionAsync(ct);
            using var reader = new StreamReader(legacy);
            var message = await reader.ReadLineAsync(ct);
            return PipeArgumentSerializer.Deserialize(Assert.IsType<string>(message));
        }
    }

    private static Task<PlayerShutdownResult> RequestAsync(string pipe, CancellationToken ct) =>
        PlayerProcessBridge.SendShutdownCommandAsync(pipe, Deadline, Deadline, ct);

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource<bool> NewResult() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static string NewPipeName() => $"Kiriha.Shutdown.{Guid.NewGuid():N}";

    public void Dispose() => _directory.Delete(recursive: true);
}
