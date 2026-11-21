using System.Collections.Concurrent;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Infrastructure.Player;
using Kiriha.Mpv;
using Kiriha.Mpv.UI.ViewModels.Player;
using Kiriha.Services;
using Kiriha.Services.AppLifecycle;

namespace Kiriha.Tests;

public sealed partial class MpvSourceSwitchTests
{
    [Fact]
    public async Task PlaybackCommand_ReportsNativeReadinessAndIsolatesACancelledOrFailedReplacement()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        using var network = new LoopbackMedia();
        await using var native = new NativePlayer(observe: false);
        var callbacks = new ConcurrentQueue<Action>();
        using var player = new PlayerViewModel(string.Empty, null, null, null, ILocalizer.Passthrough)
        {
            PlayerAutoPlay = false,
            SmartTrackAutoload = false,
            RememberPlaybackPosition = false
        };
        player.WaitForRenderContext();
        player.AttachPlayback(native.Player, callbacks.Enqueue);
        native.Observe();
        var registry = new PlayerPlaybackRequests();
        var stateGate = new Lock();
        var queriedWhileOpening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipe = $"Kiriha.NativeOpen.{Guid.NewGuid():N}";
        const string firstId = "first-source";
        const string secondId = "second-source";

        await using var server = new PlayerCommandServer(_ => { }, pipe, Deadline, handlePlayback: (frame, _) =>
        {
            lock (stateGate)
            {
                var status = registry.Handle(frame, false, (args, id) =>
                {
                    var url = PlayerModeCoordinator.GetArgValue(args, "--player") ?? string.Empty;
                    var subtitle = PlayerModeCoordinator.GetArgValue(args, "--sub-file");
                    player.LoadVideo(url, subtitle, PlayerModeCoordinator.ExtractMetadataFromArgs(args),
                        PlayerModeCoordinator.ExtractHeadersFromArgs(args), id);
                    return new(() => player.GetOpeningState(id) switch
                    {
                        PlaybackOpeningState.Ready => PlayerLaunchStatus.Ready,
                        PlaybackOpeningState.Failed => PlayerLaunchStatus.Failed,
                        PlaybackOpeningState.Pending => PlayerLaunchStatus.Accepted,
                        _ => PlayerLaunchStatus.Superseded
                    }, () => throw new InvalidOperationException("The current opening should not be cancelled."));
                });
                if (frame is [PlayerPlaybackProtocol.Query, firstId] && status == PlayerLaunchStatus.Accepted)
                    queriedWhileOpening.TrySetResult();
                return Task.FromResult(status);
            }
        });
        server.Start();
        var client = new PlayerLaunchClient(() => true, () => false, ExchangeAsync);

        var open = client.OpenAsync(firstId,
        [
            "--player", network.Url, PlayerProcessBridge.PlaybackRequestArg, firstId,
            "--title-en", "First title", "--episode", "3", "--anime-id", "77",
            "--sub-file", "first.srt", "--http-header", "X-Kiriha-Test=first"
        ], ct);
        await queriedWhileOpening.Task.WaitAsync(ct);
        Assert.False(open.IsCompleted);
        lock (stateGate)
            player.StartInitialPlayback();

        using (var connection = await network.AcceptAsync(ct))
        {
            Assert.Contains("X-Kiriha-Test: first", await ReadRequestAsync(connection, ct), StringComparison.OrdinalIgnoreCase);
            Assert.False(open.IsCompleted);
            await WriteWaveAsync(connection, truncate: false, ct);
        }
        await native.ReadAsync<MpvSourceEvent.Loaded>(ct);
        lock (stateGate)
            DrainCallbacks(callbacks);
        Assert.Equal(PlayerLaunchStatus.Ready, await open.WaitAsync(ct));

        var missing = Path.Combine(_directory.FullName, "missing.wav");
        var failed = client.OpenAsync(secondId, ["--player", missing, PlayerProcessBridge.PlaybackRequestArg, secondId], ct);
        await native.ReadAsync<MpvSourceEvent.Ended>(ct);
        lock (stateGate)
            DrainCallbacks(callbacks);
        Assert.Equal(PlayerLaunchStatus.Failed, await failed.WaitAsync(ct));
        Assert.Equal(PlayerLaunchStatus.Superseded, await ExchangeAsync(PlayerPlaybackProtocol.CreateCancel(firstId), ct));
        Assert.Null(player.StreamHeaders);
        Assert.Null(player.PendingSubtitleUrl);
        Assert.Null(player.AnimeId);
        Assert.Equal(secondId, player.CreatePlayerState().PlaybackRequestId);
        Assert.True(player.HasPlaybackError);

        Task<PlayerLaunchStatus> ExchangeAsync(string[] frame, CancellationToken token) =>
            PlayerProcessBridge.ExchangePlaybackAsync(pipe, frame, Deadline, token);
    }
}
