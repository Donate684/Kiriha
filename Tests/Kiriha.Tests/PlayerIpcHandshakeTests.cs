using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Kiriha.Core.Domain.Constants;
using Kiriha.Core.Domain.Models;
using Kiriha.Core.Domain.Models.Api;
using Kiriha.Infrastructure.Player;
using Kiriha.Infrastructure.Tracking.Integration;
using Kiriha.Mpv.UI.Services;
using Xunit;

namespace Kiriha.Tests;

public sealed class PlayerIpcHandshakeTests
{
    [Fact]
    public void InternalPlayerIpcMessage_Serialization_RoundTripsCorrectly()
    {
        var original = new InternalPlayerIpcMessage
        {
            Type = InternalPlayerIpcMessage.TypeHandshake,
            Version = "1.0",
            State = new InternalPlayerState
            {
                AnimeId = 42,
                AnimeTitle = "Frieren",
                OriginalTitle = "Sousou no Frieren",
                Episode = "10",
                Position = 123.45,
                Duration = 1440.0,
                IsPlaying = true,
                IsClosed = false
            },
            Metadata = new PlayerMediaMetadata(
                OriginalTitle: "Sousou no Frieren",
                TitleRu: "Провожающая в последний путь Фрирен",
                TitleEn: "Frieren: Beyond Journey's End",
                EpisodeText: "10",
                AnimeId: 42,
                TitleRomaji: "Sousou no Frieren")
        };

        var json = JsonSerializer.Serialize(original, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);
        Assert.NotNull(json);

        var deserialized = JsonSerializer.Deserialize(json, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);
        Assert.NotNull(deserialized);
        Assert.Equal(InternalPlayerIpcMessage.TypeHandshake, deserialized.Type);
        Assert.Equal("1.0", deserialized.Version);

        Assert.NotNull(deserialized.State);
        Assert.Equal(42, deserialized.State.AnimeId);
        Assert.Equal("Frieren", deserialized.State.AnimeTitle);
        Assert.Equal(123.45, deserialized.State.Position);
        Assert.True(deserialized.State.IsPlaying);

        Assert.NotNull(deserialized.Metadata);
        Assert.Equal("Sousou no Frieren", deserialized.Metadata.OriginalTitle);
        Assert.Equal("Провожающая в последний путь Фрирен", deserialized.Metadata.TitleRu);
        Assert.Equal(42, deserialized.Metadata.AnimeId);
    }

    [Fact]
    public async Task PlayerIpc_FullHandshakeAndStateExchange_Succeeds()
    {
        var pipeName = $"Kiriha.TestPipe.{Guid.NewGuid():N}";
        using var serverCts = new CancellationTokenSource();
        var server = new InternalPlayerServer(pipeName);
        var stateReceivedTcs = new TaskCompletionSource<InternalPlayerState>();

        server.PlayerStateChanged += (s, e) =>
        {
            if (!e.IsClosed && e.AnimeId == 777)
            {
                stateReceivedTcs.TrySetResult(e);
            }
        };

        _ = server.StartAsync(serverCts.Token);

        using var client = new InternalPlayerStateClient(pipeName);
        client.Start();

        var stateToSend = new InternalPlayerState
        {
            AnimeId = 777,
            AnimeTitle = "Steins;Gate",
            OriginalTitle = "Steins;Gate - 01",
            Episode = "1",
            Position = 45.0,
            Duration = 1420.0,
            IsPlaying = true
        };

        client.Publish(stateToSend);

        var completed = await Task.WhenAny(stateReceivedTcs.Task, Task.Delay(5000));
        Assert.Same(stateReceivedTcs.Task, completed);

        var received = await stateReceivedTcs.Task;
        Assert.Equal(777, received.AnimeId);
        Assert.Equal("Steins;Gate", received.AnimeTitle);
        Assert.Equal("1", received.Episode);
        Assert.True(client.IsConnected);

        await server.StopAsync(CancellationToken.None);
        serverCts.Cancel();
    }

    [Fact]
    public async Task PlayerIpc_ServerMetadataPush_ArrivesAtClient()
    {
        var pipeName = $"Kiriha.TestPipe.{Guid.NewGuid():N}";
        using var serverCts = new CancellationTokenSource();
        var server = new InternalPlayerServer(pipeName);
        _ = server.StartAsync(serverCts.Token);

        using var client = new InternalPlayerStateClient(pipeName);
        var metadataReceivedTcs = new TaskCompletionSource<PlayerMediaMetadata>();

        client.MetadataReceived += meta =>
        {
            if (meta.AnimeId == 999)
                metadataReceivedTcs.TrySetResult(meta);
        };

        client.Start();

        // Wait until client establishes handshake
        for (int i = 0; i < 50 && !client.IsConnected; i++)
        {
            await Task.Delay(100);
        }
        Assert.True(client.IsConnected, "Client should be connected and handshake confirmed");

        var metaToSend = new PlayerMediaMetadata(
            OriginalTitle: "Cowboy Bebop - 05",
            TitleRu: "Ковбой Бибоп",
            TitleEn: "Cowboy Bebop",
            EpisodeText: "5",
            AnimeId: 999,
            TitleRomaji: "Cowboy Bebop");

        await server.SendMetadataAsync(metaToSend);

        var completed = await Task.WhenAny(metadataReceivedTcs.Task, Task.Delay(5000));
        Assert.Same(metadataReceivedTcs.Task, completed);

        var received = await metadataReceivedTcs.Task;
        Assert.Equal("Ковбой Бибоп", received.TitleRu);
        Assert.Equal("Cowboy Bebop", received.TitleEn);
        Assert.Equal(999, received.AnimeId);

        await server.StopAsync(CancellationToken.None);
        serverCts.Cancel();
    }

    [Fact]
    public async Task PlayerIpc_PendingState_DeliveredAfterServerStarts()
    {
        var pipeName = $"Kiriha.TestPipe.{Guid.NewGuid():N}";
        using var client = new InternalPlayerStateClient(pipeName);

        var stateToSend = new InternalPlayerState
        {
            AnimeId = 555,
            AnimeTitle = "Delayed Server Anime",
            OriginalTitle = "delayed.mkv",
            Episode = "3",
            Position = 12.0,
            Duration = 1200.0,
            IsPlaying = true
        };

        // Client publishes BEFORE server is started!
        client.Publish(stateToSend);
        Assert.False(client.IsConnected);

        // Wait a moment while client is queuing state
        await Task.Delay(300);

        // Now start server
        using var serverCts = new CancellationTokenSource();
        var server = new InternalPlayerServer(pipeName);
        var stateReceivedTcs = new TaskCompletionSource<InternalPlayerState>();

        server.PlayerStateChanged += (s, e) =>
        {
            if (!e.IsClosed && e.AnimeId == 555)
            {
                stateReceivedTcs.TrySetResult(e);
            }
        };

        _ = server.StartAsync(serverCts.Token);

        var completed = await Task.WhenAny(stateReceivedTcs.Task, Task.Delay(5000));
        Assert.Same(stateReceivedTcs.Task, completed);

        var received = await stateReceivedTcs.Task;
        Assert.Equal(555, received.AnimeId);
        Assert.Equal("Delayed Server Anime", received.AnimeTitle);

        await server.StopAsync(CancellationToken.None);
        serverCts.Cancel();
    }

    [Fact]
    public async Task PlayerIpc_StateQueuedDuringHandshake_UsesSameConnectionUntilDisconnect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        string pipeName = $"Kiriha.ClientReconnect.{Guid.NewGuid():N}";
        await using var first = CreateListener();
        using var client = new InternalPlayerStateClient(pipeName);
        var firstConnect = first.WaitForConnectionAsync(ct);
        client.Publish(new InternalPlayerState { OriginalTitle = "first.mkv", PlaybackRequestId = "first" });
        await firstConnect;

        using var firstReader = new StreamReader(first, leaveOpen: true);
        var handshake = await ReadAsync(firstReader);
        Assert.Equal(InternalPlayerIpcMessage.TypeHandshake, handshake.Type);
        Assert.Equal("first", handshake.State?.PlaybackRequestId);

        // Publish queues a reconnect wake-up while this connection is still awaiting its acknowledgement.
        client.Publish(new InternalPlayerState { OriginalTitle = "second.mkv", PlaybackRequestId = "second", Position = 18 });
        await using var next = CreateListener();
        var nextConnect = next.WaitForConnectionAsync(ct);
        var ack = JsonSerializer.Serialize(new InternalPlayerIpcMessage { Type = InternalPlayerIpcMessage.TypeHandshakeAck },
            InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);
        await using (var firstWriter = new StreamWriter(first, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
            await firstWriter.WriteLineAsync(ack.AsMemory(), ct);

        var update = await ReadAsync(firstReader);
        Assert.Equal(InternalPlayerIpcMessage.TypeState, update.Type);
        Assert.Equal("second", update.State?.PlaybackRequestId);
        await Assert.ThrowsAsync<TimeoutException>(() => nextConnect.WaitAsync(TimeSpan.FromMilliseconds(250), ct));

        first.Disconnect();
        await nextConnect;
        using var nextReader = new StreamReader(next, leaveOpen: true);
        var resumed = await ReadAsync(nextReader);
        Assert.Equal(InternalPlayerIpcMessage.TypeHandshake, resumed.Type);
        Assert.Equal("second", resumed.State?.PlaybackRequestId);
        Assert.Equal(18, resumed.State?.Position);

        NamedPipeServerStream CreateListener() => new(pipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        async Task<InternalPlayerIpcMessage> ReadAsync(StreamReader reader)
        {
            var line = await reader.ReadLineAsync(ct);
            Assert.NotNull(line);
            var message = JsonSerializer.Deserialize(line, InternalPlayerStateJsonContext.Default.InternalPlayerIpcMessage);
            Assert.NotNull(message);
            return message;
        }
    }

    [Fact]
    public void PlayerProcessBridge_IsMainAppRunning_WorksWithMutex()
    {
        if (!OperatingSystem.IsWindows()) return;

        // When mutex does not exist
        _ = PlayerProcessBridge.IsMainAppRunning();

        // Create mutex pretending main app is running
        using var mutex = new Mutex(true, AppConstants.System.MutexName, out var createdNew);
        if (createdNew)
        {
            Assert.True(PlayerProcessBridge.IsMainAppRunning(), "Should detect main app mutex when created");
        }
    }
}
