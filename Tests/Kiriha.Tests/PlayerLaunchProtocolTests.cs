using System.IO.Pipes;
using Kiriha.Infrastructure.Player;
using Kiriha.Services;
using Kiriha.Services.AppLifecycle;

namespace Kiriha.Tests;

public sealed class PlayerLaunchProtocolTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(1);
    private const string RequestId = "episode-a";

    [Fact]
    public async Task LostAcknowledgment_ReplaysTheSameRequestWithoutOpeningAnotherWindow()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var ct = timeout.Token;
        var pipe = NewPipeName();
        var requests = new PlayerPlaybackRequests();
        var opened = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var args = Arguments(RequestId);
        var command = PlayerPlaybackProtocol.CreateOpen(RequestId, args);

        PlayerPlaybackRequests.Target Open(string[] received, string id)
        {
            Assert.Equal(RequestId, id);
            Assert.Equal(args, received);
            Interlocked.Increment(ref opened);
            return new(
                () => ready.Task.IsCompleted ? PlayerLaunchStatus.Ready : PlayerLaunchStatus.Accepted,
                () => throw new InvalidOperationException("Opening was not cancelled."));
        }

        // Accept the first command and drop its connection without writing a reply.
        using var first = CreateRawServer(pipe);
        var firstExchange = ReadFrameAsync(first, ct);
        var firstReply = ExchangeAsync(pipe, command, ct);
        Assert.Equal(PlayerLaunchStatus.Accepted, requests.Handle(await firstExchange, false, Open));
        first.Dispose();
        Assert.Equal(PlayerLaunchStatus.Unconfirmed, await firstReply);

        await using var server = new PlayerCommandServer(_ => { }, pipe, ExchangeTimeout, handlePlayback: (frame, _) =>
        {
            var result = requests.Handle(frame, false, Open);
            if (frame[0] == PlayerPlaybackProtocol.Query)
                queried.TrySetResult();
            return Task.FromResult(result);
        });
        server.Start();

        var client = new PlayerLaunchClient(() => true, () => false,
            (frame, token) => ExchangeAsync(pipe, frame, token));
        var opening = client.OpenAsync(RequestId, args, ct);
        await queried.Task.WaitAsync(ct);
        Assert.False(opening.IsCompleted);
        Assert.Equal(1, Volatile.Read(ref opened));

        ready.TrySetResult();
        Assert.Equal(PlayerLaunchStatus.Ready, await opening.WaitAsync(ct));
        Assert.Equal(1, Volatile.Read(ref opened));
    }

    [Fact]
    public async Task ReceiverExitsBeforeAccepting_ReplacementAcceptsTheOriginalPayloadOnce()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var ct = timeout.Token;
        var pipe = NewPipeName();
        var args = Arguments(RequestId);
        int running = 1;
        int starts = 0;
        int opened = 0;
        var requests = new PlayerPlaybackRequests();
        await using var replacement = new PlayerCommandServer(_ => { }, pipe, ExchangeTimeout,
            handlePlayback: (frame, _) => Task.FromResult(requests.Handle(frame, false, (payload, id) =>
            {
                Assert.Equal(args, payload);
                Assert.Equal(RequestId, id);
                Interlocked.Increment(ref opened);
                return new(() => PlayerLaunchStatus.Ready, () => { });
            })));
        using var original = CreateRawServer(pipe);
        var originalFrame = ReadFrameAsync(original, ct);
        var client = new PlayerLaunchClient(() => Volatile.Read(ref running) != 0, () =>
        {
            Interlocked.Increment(ref starts);
            Volatile.Write(ref running, 1);
            replacement.Start();
            return true;
        }, (frame, token) => ExchangeAsync(pipe, frame, token));

        var opening = client.OpenAsync(RequestId, args, ct);
        Assert.Equal(PlayerPlaybackProtocol.CreateOpen(RequestId, args), await originalFrame);
        Volatile.Write(ref running, 0);
        original.Dispose();

        Assert.Equal(PlayerLaunchStatus.Ready, await opening.WaitAsync(ct));
        Assert.Equal(1, Volatile.Read(ref starts));
        Assert.Equal(1, Volatile.Read(ref opened));
    }

    [Fact]
    public async Task ColdLaunchRegistersInitialRequestBeforeTheTrackerAttemptsReplay()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var ct = timeout.Token;
        var pipe = NewPipeName();
        var requests = new PlayerPlaybackRequests();
        int started = 0;
        int opened = 0;

        PlayerPlaybackRequests.Target Open()
        {
            Interlocked.Increment(ref opened);
            return new(() => PlayerLaunchStatus.Ready, () => { });
        }

        await using var server = new PlayerCommandServer(_ => { }, pipe, ExchangeTimeout,
            handlePlayback: (frame, _) => Task.FromResult(requests.Handle(frame, false, (_, _) => Open())));
        var client = new PlayerLaunchClient(() => Volatile.Read(ref started) != 0, () =>
        {
            Interlocked.Increment(ref started);
            requests.Open(RequestId, expired: false, Open);
            server.Start();
            return true;
        }, (frame, token) => ExchangeAsync(pipe, frame, token));

        Assert.Equal(PlayerLaunchStatus.Ready, await client.OpenAsync(RequestId, Arguments(RequestId), ct));
        Assert.Equal(1, Volatile.Read(ref started));
        Assert.Equal(1, Volatile.Read(ref opened));
    }

    [Fact]
    public async Task CancellingAnOlderOpeningLeavesTheCurrentRequestIntact()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var ct = timeout.Token;
        var pipe = NewPipeName();
        var requests = new PlayerPlaybackRequests();
        const string currentId = "episode-b";
        string? selected = null;
        var queried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellations = new List<string>();

        await using var server = new PlayerCommandServer(_ => { }, pipe, ExchangeTimeout, handlePlayback: (frame, _) =>
        {
            var result = requests.Handle(frame, false, (_, id) =>
            {
                selected = id;
                return new(() => selected == id ? PlayerLaunchStatus.Accepted : PlayerLaunchStatus.Superseded,
                    () => cancellations.Add(id));
            });
            if (frame is [PlayerPlaybackProtocol.Query, RequestId])
                queried.TrySetResult();

            return Task.FromResult(result);
        });
        server.Start();

        var client = new PlayerLaunchClient(() => true, () => false, async (frame, token) =>
        {
            var status = await ExchangeAsync(pipe, frame, token);
            if (frame is [PlayerPlaybackProtocol.Query, RequestId])
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return status;
        });
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var first = client.OpenAsync(RequestId, Arguments(RequestId), cancel.Token);
        await queried.Task.WaitAsync(ct);
        Assert.Equal(PlayerLaunchStatus.Accepted,
            await ExchangeAsync(pipe, PlayerPlaybackProtocol.CreateOpen(currentId, Arguments(currentId)), ct));
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        Assert.Equal(PlayerLaunchStatus.Superseded,
            await ExchangeAsync(pipe, PlayerPlaybackProtocol.CreateOpen(RequestId, Arguments(RequestId)), ct));
        Assert.Equal(PlayerLaunchStatus.Accepted, await ExchangeAsync(pipe, PlayerPlaybackProtocol.CreateQuery(currentId), ct));
        Assert.Empty(cancellations);
        Assert.Equal(PlayerLaunchStatus.Superseded,
            await ExchangeAsync(pipe, PlayerPlaybackProtocol.CreateCancel(currentId), ct));
        Assert.Equal([currentId], cancellations);
    }

    [Fact]
    public async Task OpeningTimeoutCancelsOnlyTheStillPendingRequest()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var ct = timeout.Token;
        var pipe = NewPipeName();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new PlayerPlaybackRequests();
        await using var server = new PlayerCommandServer(_ => { }, pipe, ExchangeTimeout,
            handlePlayback: (frame, _) => Task.FromResult(requests.Handle(frame, false, (_, _) =>
                new(() => PlayerLaunchStatus.Accepted, () => cancelled.TrySetResult()))));
        server.Start();

        var client = new PlayerLaunchClient(() => true, () => false, (frame, token) => ExchangeAsync(pipe, frame, token),
            openingTimeout: TimeSpan.FromMilliseconds(300));
        Assert.Equal(PlayerLaunchStatus.Unconfirmed, await client.OpenAsync(RequestId, Arguments(RequestId), ct));
        await cancelled.Task.WaitAsync(ct);
        Assert.Equal(PlayerLaunchStatus.Superseded, await ExchangeAsync(pipe, PlayerPlaybackProtocol.CreateQuery(RequestId), ct));
    }

    [Fact]
    public void LateExpiredOrPreviouslyCancelledFramesDoNotCreateWindows()
    {
        var requests = new PlayerPlaybackRequests();
        const string cancelled = "cancelled";
        const string expired = "expired";
        static PlayerPlaybackRequests.Target Unexpected(string[] _, string id) =>
            throw new InvalidOperationException($"Unexpected request {id}");

        Assert.Equal(PlayerLaunchStatus.Superseded,
            requests.Handle(PlayerPlaybackProtocol.CreateCancel(cancelled), false, Unexpected));
        Assert.Equal(PlayerLaunchStatus.Superseded,
            requests.Handle(PlayerPlaybackProtocol.CreateOpen(cancelled, Arguments(cancelled)), false, Unexpected));

        var oldArguments = Arguments(expired);
        oldArguments[^1] = "0";
        Assert.Equal(PlayerLaunchStatus.Failed,
            requests.Handle(PlayerPlaybackProtocol.CreateOpen(expired, oldArguments), false, Unexpected));
        Assert.Equal(PlayerLaunchStatus.NotFound, requests.Get(expired));
    }

    private static string[] Arguments(string id) =>
    [
        "--player", "эпизод with spaces.mkv", "--sub-file", "эпизод.ass",
        "--http-header", "X-Kiriha-Test=abc=xyz", "--episode", "7", "--anime-id", "42",
        PlayerProcessBridge.PlaybackRequestArg, id,
        PlayerPlaybackProtocol.AdmissionDeadlineArg, PlayerPlaybackProtocol.CreateAdmissionDeadline()
    ];

    private static Task<PlayerLaunchStatus> ExchangeAsync(string pipe, string[] frame, CancellationToken ct) =>
        PlayerProcessBridge.ExchangePlaybackAsync(pipe, frame, ExchangeTimeout, ct);

    private static NamedPipeServerStream CreateRawServer(string pipe) =>
        new(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static async Task<string[]> ReadFrameAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await pipe.WaitForConnectionAsync(ct);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        return PipeArgumentSerializer.Deserialize(Assert.IsType<string>(await reader.ReadLineAsync(ct)));
    }

    private static string NewPipeName() => $"Kiriha.Launch.{Guid.NewGuid():N}";
}
