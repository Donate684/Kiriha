using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Core.Domain.Models.Entities;
using Kiriha.Data.Repository;
using Kiriha.Infrastructure.Tracking.Integration;

namespace Kiriha.Tests;

public sealed partial class ScrobbleSyncRestartTests
{
    [Theory]
    [InlineData("closed")]
    [InlineData("raw")]
    [InlineData("disconnect")]
    public async Task PlayerIpc_OlderConnectionCannotUpdateOrReclaimPlayback(string closeMode)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        string pipeName = $"Kiriha.Ownership.{Guid.NewGuid():N}";
        using var server = new InternalPlayerServer(pipeName);
        var published = new ConcurrentQueue<InternalPlayerState>();
        var stateEvents = Channel.CreateUnbounded<InternalPlayerState>();
        server.PlayerStateChanged += (_, state) =>
        {
            published.Enqueue(state);
            stateEvents.Writer.TryWrite(state);
        };

        await server.StartAsync(ct);
        await using var first = await PlaybackPipe.ConnectAsync(pipeName, ct);
        await using var second = await PlaybackPipe.ConnectAsync(pipeName, ct);
        await using var earlierIdle = await PlaybackPipe.ConnectAsync(pipeName, ct);
        try
        {
            var firstState = State("first.mkv", 12);
            var secondState = State("second.mkv", 60);
            var thirdState = State("third.mkv", 5);
            await first.HandshakeAsync(firstState, ct);
            Assert.Equal(firstState.OriginalTitle, (await stateEvents.Reader.ReadAsync(ct)).OriginalTitle);

            await earlierIdle.HandshakeAsync(null, ct);
            await earlierIdle.SendStateAsync(new InternalPlayerState { IsLoading = true }, ct);
            await earlierIdle.FenceAsync(ct);
            Assert.Equal(firstState.OriginalTitle, server.LatestState?.OriginalTitle);
            Assert.False(stateEvents.Reader.TryRead(out _));

            // A new connection may still speak the legacy state format.
            await second.SendRawAsync(secondState, ct);
            Assert.Equal(secondState.OriginalTitle, (await stateEvents.Reader.ReadAsync(ct)).OriginalTitle);

            await first.SendStateAsync(State(firstState.OriginalTitle, 300), ct);
            await first.SendRawAsync(State(firstState.OriginalTitle, 310), ct);
            await first.HandshakeAsync(State(firstState.OriginalTitle, 320), ct);
            await first.FenceAsync(ct);
            Assert.Equal((secondState.OriginalTitle, secondState.Position),
                (server.LatestState?.OriginalTitle, server.LatestState?.Position));
            Assert.False(stateEvents.Reader.TryRead(out _));

            await first.CloseAsync(ct);
            await first.SendStateAsync(State(firstState.OriginalTitle, 500), ct);
            await first.FenceAsync(ct);
            Assert.Equal(secondState.OriginalTitle, server.LatestState?.OriginalTitle);
            Assert.False(stateEvents.Reader.TryRead(out _));

            await earlierIdle.SendStateAsync(thirdState, ct);
            Assert.Equal(thirdState.OriginalTitle, (await stateEvents.Reader.ReadAsync(ct)).OriginalTitle);
            if (closeMode == "closed")
                await earlierIdle.CloseAsync(ct);
            else if (closeMode == "raw")
                await earlierIdle.SendRawAsync(new InternalPlayerState { IsClosed = true }, ct);
            else
                earlierIdle.Disconnect();

            Assert.True((await stateEvents.Reader.ReadAsync(ct)).IsClosed);
            Assert.Null(server.LatestState);
            // The previous owner is connected and hasn't sent a close. It still can't reclaim playback.
            await second.SendStateAsync(State(secondState.OriginalTitle, 700), ct);
            await second.FenceAsync(ct);
            Assert.Null(server.LatestState);
            Assert.False(stateEvents.Reader.TryRead(out _));
            if (closeMode != "disconnect")
            {
                await earlierIdle.SendStateAsync(thirdState, ct);
                await earlierIdle.FenceAsync(ct);
                Assert.Null(server.LatestState);
            }

            await using var replacement = await PlaybackPipe.ConnectAsync(pipeName, ct);
            await replacement.HandshakeAsync(State("replacement.mkv", 8), ct);
            Assert.Equal("replacement.mkv", (await stateEvents.Reader.ReadAsync(ct)).OriginalTitle);
            await server.StopAsync(ct);

            Assert.Null(server.LatestState);
            Assert.False(server.HasConnectedClients);
            Assert.Equal(2, published.Count(state => state.IsClosed));
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }

        static InternalPlayerState State(string title, double position) => new()
        {
            OriginalTitle = title, AnimeTitle = title, Episode = "1", Position = position, IsPlaying = true
        };
    }

    [Fact]
    public async Task PlayerIpc_LateStateAndCloseFromFirstPlayer_LeaveSecondCountdownRunning()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        await using var scenario = await OpenLibraryScenarioAsync(ct);
        var firstAnime = await AddCountdownAnimeAsync(scenario, ct);
        var secondAnime = firstAnime.Clone();
        secondAnime.Id++;
        secondAnime.Title = "Second title";
        Assert.True((await scenario.Actions.AddToListAsync(secondAnime, UserAnimeStatus.Watching, 2, ct)).Success);

        string pipeName = $"Kiriha.Playback.{Guid.NewGuid():N}";
        using var server = new InternalPlayerServer(pipeName);
        await using var playback = new CountdownPlayback(scenario, new SqliteScrobbleProgressStore(CreateFactory()),
            delaySeconds: 4, playerServer: server);
        await server.StartAsync(ct);
        await using var first = await PlaybackPipe.ConnectAsync(pipeName, ct);
        await using var second = await PlaybackPipe.ConnectAsync(pipeName, ct);
        try
        {
            var firstState = PlayerState(firstAnime);
            firstState.OriginalTitle = "first.mkv";
            var secondState = PlayerState(secondAnime, secondSource: true);
            secondState.OriginalTitle = "second.mkv";
            secondState.Position = 42;

            await first.HandshakeAsync(firstState, ct);
            var firstTick = await playback.Clock.NextTickAsync(ct);
            playback.Clock.Advance(TimeSpan.FromSeconds(1));
            await second.HandshakeAsync(secondState, ct);
            await second.FenceAsync(ct);
            var secondTick = await playback.Clock.NextTickAsync(ct);
            await playback.Countdowns[0].WaitAsync(ct);

            firstState.Position = 99;
            firstState.IsPlaying = false;
            await first.SendStateAsync(firstState, ct);
            await first.CloseAsync(ct);
            await first.FenceAsync(ct);
            first.Disconnect();
            Assert.Equal(secondState.OriginalTitle, playback.Tracking.CurrentMedia?.OriginalTitle);
            Assert.Equal(TimeSpan.FromSeconds(42), playback.Tracking.CurrentMedia?.Position);
            Assert.Same(secondAnime, playback.Tracking.MatchedAnime);
            Assert.Equal(2, playback.Countdowns.Count);

            playback.Clock.Advance(TimeSpan.FromSeconds(4));
            firstTick();
            secondTick();
            await playback.Countdowns[1].WaitAsync(ct);
            Assert.Equal(2, (await scenario.Store.GetByIdAsync(firstAnime.Id, ct: ct))?.Progress);
            Assert.Equal(3, (await scenario.Store.GetByIdAsync(secondAnime.Id, ct: ct))?.Progress);

            secondState.Episode = "4";
            await second.SendStateAsync(secondState, ct);
            var finalTick = await playback.Clock.NextTickAsync(ct);
            await second.CloseAsync(ct);
            await second.FenceAsync(ct);
            playback.Clock.Advance(TimeSpan.FromSeconds(4));
            finalTick();
            await playback.Countdowns[2].WaitAsync(ct);

            Assert.Null(playback.Tracking.CurrentMedia);
            Assert.Null(server.LatestState);
            Assert.Equal(3, (await scenario.Store.GetByIdAsync(secondAnime.Id, ct: ct))?.Progress);
            await AssertLibraryActionsAsync(scenario, actions: 3, scrobbles: 1, ct);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task PlayerIpc_ConcurrentReaders_PublishOwnershipChangesInOrder()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var ct = timeout.Token;
        string pipeName = $"Kiriha.StateOrder.{Guid.NewGuid():N}";
        using var server = new InternalPlayerServer(pipeName);
        using var resumeFirstHandler = new ManualResetEventSlim();
        var firstHandlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new ConcurrentQueue<string>();
        server.PlayerStateChanged += (_, state) =>
        {
            if (state.OriginalTitle == "first.mkv")
            {
                firstHandlerEntered.TrySetResult();
                resumeFirstHandler.Wait(ct);
            }

            if (!state.IsClosed)
                received.Enqueue(state.OriginalTitle);
        };

        await server.StartAsync(ct);
        await using var first = await PlaybackPipe.ConnectAsync(pipeName, ct);
        await using var second = await PlaybackPipe.ConnectAsync(pipeName, ct);
        try
        {
            await first.HandshakeAsync(new InternalPlayerState { OriginalTitle = "first.mkv" }, ct);
            await firstHandlerEntered.Task.WaitAsync(ct);
            await second.HandshakeAsync(new InternalPlayerState { OriginalTitle = "second.mkv" }, ct);

            Assert.Equal("first.mkv", server.LatestState?.OriginalTitle);
            Assert.Empty(received);
            resumeFirstHandler.Set();
            await second.FenceAsync(ct);

            Assert.Equal(["first.mkv", "second.mkv"], received.ToArray());
            Assert.Equal("second.mkv", server.LatestState?.OriginalTitle);
        }
        finally
        {
            resumeFirstHandler.Set();
            await server.StopAsync(CancellationToken.None);
        }
    }

    private sealed class PlaybackPipe : IAsyncDisposable
    {
        private readonly NamedPipeClientStream _pipe;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        private PlaybackPipe(NamedPipeClientStream pipe)
        {
            _pipe = pipe;
            var encoding = new UTF8Encoding(false);
            _reader = new StreamReader(pipe, encoding, leaveOpen: true);
            _writer = new StreamWriter(pipe, encoding, leaveOpen: true) { AutoFlush = true };
        }

        internal static async Task<PlaybackPipe> ConnectAsync(string name, CancellationToken ct)
        {
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(ct);
                return new PlaybackPipe(pipe);
            }
            catch
            {
                await pipe.DisposeAsync();
                throw;
            }
        }

        internal async Task<PlayerMediaMetadata?> HandshakeAsync(InternalPlayerState? state, CancellationToken ct)
        {
            await SendAsync(InternalPlayerIpcMessage.TypeHandshake, state, ct);
            return (await ReadReplyAsync(InternalPlayerIpcMessage.TypeHandshakeAck, ct)).Metadata;
        }

        internal Task SendStateAsync(InternalPlayerState state, CancellationToken ct) =>
            SendAsync(InternalPlayerIpcMessage.TypeState, state, ct);

        internal Task CloseAsync(CancellationToken ct) => SendAsync(InternalPlayerIpcMessage.TypeClosed, null, ct);

        internal async Task FenceAsync(CancellationToken ct)
        {
            await SendAsync(InternalPlayerIpcMessage.TypePing, null, ct);
            await ReadReplyAsync(InternalPlayerIpcMessage.TypePong, ct);
        }

        internal async Task<IReadOnlyList<PlayerMediaMetadata>> ReadMetadataAsync(CancellationToken ct)
        {
            var updates = new List<PlayerMediaMetadata>();
            var reply = ReadReplyAsync(InternalPlayerIpcMessage.TypePong, ct, updates);
            await SendAsync(InternalPlayerIpcMessage.TypePing, null, ct);
            await reply;
            return updates;
        }

        internal async Task<PlayerMediaMetadata> ReadMetadataFrameAsync(CancellationToken ct)
        {
            var message = await ReadReplyAsync(InternalPlayerIpcMessage.TypeMetadata, ct);
            Assert.NotNull(message.Metadata);
            return message.Metadata;
        }

        internal Task SendRawAsync(InternalPlayerState state, CancellationToken ct) =>
            _writer.WriteLineAsync(JsonSerializer.Serialize(state, InternalPlayerStateJsonContext.Default.InternalPlayerState).AsMemory(), ct);

        private Task SendAsync(string type, InternalPlayerState? state, CancellationToken ct)
        {
            var message = new InternalPlayerIpcMessage { Type = type, State = state };
            var json = JsonSerializer.Serialize(message, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);
            return _writer.WriteLineAsync(json.AsMemory(), ct);
        }

        private async Task<InternalPlayerIpcMessage> ReadReplyAsync(
            string replyType, CancellationToken ct, List<PlayerMediaMetadata>? updates = null)
        {
            while (true)
            {
                var line = await _reader.ReadLineAsync(ct);
                Assert.NotNull(line);
                var message = JsonSerializer.Deserialize(line, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);
                Assert.NotNull(message);
                if (message.Type == replyType)
                    return message;
                if (message.Type == InternalPlayerIpcMessage.TypeMetadata)
                {
                    Assert.NotNull(message.Metadata);
                    updates?.Add(message.Metadata);
                    continue;
                }

                Assert.Equal(replyType, message.Type);
                throw new InvalidOperationException("Unexpected IPC reply.");
            }
        }

        internal void Disconnect() => _pipe.Dispose();

        public async ValueTask DisposeAsync()
        {
            // AutoFlush already sent each frame. On abrupt disconnect there may be no writable pipe.
            _reader.Dispose();
            if (_pipe.IsConnected)
                await _writer.DisposeAsync();
            await _pipe.DisposeAsync();
        }
    }
}
