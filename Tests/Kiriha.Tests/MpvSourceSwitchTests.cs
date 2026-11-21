using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Mpv;
using Kiriha.Mpv.UI.ViewModels.Player;
using Moq;

namespace Kiriha.Tests;

public sealed partial class MpvSourceSwitchTests : IDisposable
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("Kiriha.MpvSources.");

    [Fact]
    public void PlaylistEvents_RejectObsoleteEntriesAndFollowRedirectedEntries()
    {
        var sources = new MpvPlaylistSource();
        var obsolete = sources.Reserve();
        sources.Register(obsolete, 10);
        sources.Start(10);
        Assert.Equal(obsolete, sources.StartedSource);

        var selected = sources.Reserve();
        Assert.Equal(0, sources.StartedSource);
        Assert.Equal(0, sources.End(10, 20, 2));

        sources.Register(selected, 11);
        sources.Start(10);
        Assert.Equal(0, sources.StartedSource);
        sources.Start(11);
        Assert.Equal(selected, sources.End(11, 100, 2));
        sources.Start(100);
        Assert.Equal(selected, sources.End(100, 200, 1));
        sources.Start(200);
        Assert.Equal(selected, sources.StartedSource);
        Assert.Equal(0, sources.End(10, 0, 0));
        Assert.Equal(selected, sources.End(200, 0, 0));
        sources.Start(101);
        Assert.Equal(selected, sources.StartedSource);
    }

    [Fact]
    public async Task NativePlayer_ReplacesSourceWhileItsNetworkRequestIsStillOpen()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        using var network = new LoopbackMedia();
        var path = Path.Combine(_directory.FullName, "replacement.wav");
        await File.WriteAllBytesAsync(path, CreateWave(), ct);
        await using var fixture = new NativePlayer();
        var oldSource = fixture.Player.LoadSource(network.Url);

        // The first native command has reached the demuxer and is waiting for response headers.
        using var abandoned = await network.AcceptAsync(ct);
        Assert.Contains("GET /episode.wav", await ReadRequestAsync(abandoned, ct));
        var replacement = fixture.Player.LoadSource(path);
        abandoned.Dispose();

        var loaded = await fixture.ReadAsync<MpvSourceEvent.Loaded>(ct, notification =>
        {
            Assert.NotEqual(oldSource, notification.SourceId);
        });
        Assert.Equal(replacement, loaded.SourceId);
        Assert.InRange(loaded.Playback.Duration, 11.9, 12.1);
    }

    [Fact]
    public async Task NativePlayer_CanRetryTheSameUrlAfterOpenFailure()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        var path = Path.Combine(_directory.FullName, "retry.wav");
        await using var fixture = new NativePlayer();

        var failed = fixture.Player.LoadSource(path);
        var end = await fixture.ReadAsync<MpvSourceEvent.Ended>(ct);
        Assert.Equal(failed, end.SourceId);
        Assert.True(end.Details.HasError);

        await File.WriteAllBytesAsync(path, CreateWave(), ct);
        var retry = fixture.Player.LoadSource(path);
        var loaded = await fixture.ReadAsync<MpvSourceEvent.Loaded>(ct);
        Assert.NotEqual(failed, retry);
        Assert.Equal(retry, loaded.SourceId);
        Assert.InRange(loaded.Playback.Duration, 11.9, 12.1);
    }

    [Fact]
    public async Task NativePlayer_ReportsInterruptedStreamForTheSourceThatWasAlreadyLoaded()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        using var network = new LoopbackMedia();
        await using var fixture = new NativePlayer(observe: false);
        var callbacks = new ConcurrentQueue<Action>();
        var resume = new Mock<IPlaybackResumeStore>();
        using var viewModel = new PlayerViewModel(network.Url, null, null, null, ILocalizer.Passthrough,
            resumeStore: resume.Object);
        viewModel.PlayerAutoPlay = false;
        viewModel.SmartTrackAutoload = false;
        viewModel.RememberPlaybackPosition = true;
        viewModel.AttachPlayback(fixture.Player, callbacks.Enqueue);
        fixture.Observe();
        viewModel.StartInitialPlayback();

        long sourceId;
        using (var connection = await network.AcceptAsync(ct))
        {
            await ReadRequestAsync(connection, ct);
            await WriteWaveAsync(connection, truncate: true, ct);
            var loaded = await fixture.ReadAsync<MpvSourceEvent.Loaded>(ct);
            sourceId = loaded.SourceId;
            Assert.InRange(loaded.Playback.Duration, 11.9, 12.1);
            DrainCallbacks(callbacks);
            Assert.False(viewModel.IsLoading);
        }

        network.Dispose();
        fixture.Player.SetSpeed(5);
        fixture.Player.Play();
        var interruption = await fixture.ReadTerminalAsync(ct);
        Assert.Equal(sourceId, interruption.SourceId);
        var finalState = interruption switch
        {
            MpvSourceEvent.Ended ended => ended.Playback,
            MpvSourceEvent.Eof eof => eof.Playback,
            _ => throw new InvalidOperationException("The native player did not report a terminal event.")
        };
        Assert.InRange(finalState.Position, 5, 10);
        DrainCallbacks(callbacks);
        Assert.True(viewModel.HasPlaybackError);
        Assert.False(viewModel.IsPlaying);
        resume.Verify(store => store.SavePosition(network.Url, It.IsInRange(5d, 10d, Moq.Range.Inclusive),
            It.IsInRange(11.9, 12.1, Moq.Range.Inclusive)), Times.Once);
        resume.Verify(store => store.Remove(network.Url), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlaybackFailure_PreservesResumeAndReloadIgnoresOldEvents(bool prematureEof)
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        using var network = new LoopbackMedia();
        await using var fixture = new NativePlayer(observe: false);
        var callbacks = new ConcurrentQueue<Action>();
        var resume = new Mock<IPlaybackResumeStore>();
        double? saved = null;
        resume.Setup(store => store.TryGetResumePosition(network.Url, It.IsAny<string?>()))
            .Returns(() => saved);
        resume.Setup(store => store.SavePosition(network.Url, It.IsAny<double>(), It.IsAny<double>()))
            .Callback((string _, double position, double _) => saved = position);
        IReadOnlyDictionary<string, string> headers = new Dictionary<string, string>
        {
            ["Referer"] = "https://stream.example.test/watch",
            ["X-Kiriha-Test"] = "retained-on-retry"
        };
        var metadata = new PlayerMediaMetadata("test episode", "", "", "3", AnimeId: 77, TitleRomaji: "Test Romaji");
        using var viewModel = new PlayerViewModel(network.Url, metadata, null, null, ILocalizer.Passthrough,
            subtitleUrl: "caption.srt", headers: headers, resumeStore: resume.Object);
        viewModel.PlayerAutoPlay = false;
        viewModel.SmartTrackAutoload = false;
        viewModel.RememberPlaybackPosition = true;
        viewModel.AttachPlayback(fixture.Player, callbacks.Enqueue);
        fixture.Observe();

        viewModel.StartInitialPlayback();
        await network.ServeWaveAsync(ct);
        var loaded = await fixture.ReadAsync<MpvSourceEvent.Loaded>(ct);
        DrainCallbacks(callbacks);
        Assert.False(viewModel.IsLoading);

        var playback = new PlaybackState(6, 12, true, true, true);
        viewModel.ApplyNativeSourceEvent(new MpvSourceEvent.State(loaded.SourceId, playback));
        viewModel.ApplyNativeSourceEvent(new MpvSourceEvent.Buffering(loaded.SourceId, true));
        var stopped = new MpvPlaybackEndedEventArgs(MpvPlaybackEndedEventArgs.ReasonError, 0);
        if (prematureEof)
            viewModel.ApplyNativeSourceEvent(new MpvSourceEvent.Eof(loaded.SourceId, true, playback with { Duration = 6 }));
        else
            viewModel.ApplyNativeSourceEvent(new MpvSourceEvent.Ended(loaded.SourceId, stopped, playback));

        Assert.True(viewModel.HasPlaybackError);
        Assert.False(viewModel.IsPlaying);
        Assert.False(viewModel.IsBuffering);
        var failedState = viewModel.CreatePlayerState();
        Assert.True(failedState.HasPlaybackError);
        Assert.False(failedState.IsPlaying);
        resume.Verify(store => store.SavePosition(network.Url, 6, 12), Times.Once);

        viewModel.ReloadMedia();
        viewModel.ApplyNativeSourceEvent(loaded);
        viewModel.ApplyNativeSourceEvent(new MpvSourceEvent.Eof(loaded.SourceId, true, playback with { Position = 12 }));
        viewModel.ApplyNativeSourceEvent(new MpvSourceEvent.Ended(loaded.SourceId, stopped, playback));
        viewModel.ApplyNativeSourceEvent(new MpvSourceEvent.State(loaded.SourceId, playback));
        viewModel.ApplyNativeSourceEvent(new MpvSourceEvent.Buffering(loaded.SourceId, true));
        Assert.True(viewModel.IsLoading);
        Assert.False(viewModel.HasPlaybackError);
        Assert.False(viewModel.IsBuffering);
        Assert.Equal(0, viewModel.CurrentTime);
        Assert.Equal("caption.srt", viewModel.PendingSubtitleUrl);
        resume.Verify(store => store.Remove(network.Url), Times.Never);

        var request = await network.ServeWaveAsync(ct);
        Assert.Contains("X-Kiriha-Test: retained-on-retry", request, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Referer: https://stream.example.test/watch", request, StringComparison.OrdinalIgnoreCase);
        var retried = await fixture.ReadAsync<MpvSourceEvent.Loaded>(ct);
        // The native file-loaded notification can arrive before the HTTP stream's full duration is known.
        var initialEstimate = retried.Playback with { Position = 0, Duration = 4 };
        viewModel.ApplyNativeSourceEvent(retried with { Playback = initialEstimate });
        Assert.NotEqual(loaded.SourceId, retried.SourceId);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.HasPlaybackError);
        Assert.Equal(77, viewModel.AnimeId);
        Assert.Equal("3", viewModel.RawEpisodeText);
        Assert.False(viewModel.Overlay.IsResumePromptVisible);

        viewModel.ApplyNativeSourceEvent(new MpvSourceEvent.State(loaded.SourceId, playback));
        Assert.False(viewModel.Overlay.IsResumePromptVisible);
        viewModel.ApplyNativeSourceEvent(new MpvSourceEvent.State(retried.SourceId, initialEstimate with { Duration = 12 }));
        Assert.True(viewModel.Overlay.IsResumePromptVisible);
        Assert.Equal(6, viewModel.Overlay.ResumePromptPosition);
        var recoveredState = viewModel.CreatePlayerState();
        Assert.NotEqual(failedState.PlaybackRequestId, recoveredState.PlaybackRequestId);
        Assert.Equal(failedState.PlaybackSourceKey, recoveredState.PlaybackSourceKey);
        Assert.False(recoveredState.HasPlaybackError);
        Assert.False(recoveredState.IsLoading);
        resume.Verify(store => store.Remove(network.Url), Times.Never);

        viewModel.DismissResumePromptCommand.Execute(null);
        viewModel.ApplyNativeSourceEvent(new MpvSourceEvent.State(retried.SourceId, initialEstimate with { Duration = 12.5 }));
        Assert.False(viewModel.Overlay.IsResumePromptVisible);

        var completed = new MpvSourceEvent.Eof(retried.SourceId, true, playback with { Position = 12 });
        viewModel.ApplyNativeSourceEvent(completed);
        viewModel.ApplyNativeSourceEvent(completed);
        Assert.False(viewModel.Overlay.IsResumePromptVisible);
        resume.Verify(store => store.Remove(network.Url), Times.Once);
    }

    private static void DrainCallbacks(ConcurrentQueue<Action> callbacks)
    {
        while (callbacks.TryDequeue(out var callback))
            callback();
    }

    private sealed class NativePlayer : IAsyncDisposable
    {
        private readonly Channel<MpvSourceEvent> _events = Channel.CreateUnbounded<MpvSourceEvent>();
        internal MpvPlayer Player { get; } = new();

        internal NativePlayer(bool observe = true)
        {
            try
            {
                var handle = Player.MpvHandle;
                (string Name, string Value)[] options =
                [
                    ("config", "no"), ("ao", "null"), ("vo", "null"), ("idle", "yes"), ("keep-open", "yes"), ("pause", "yes")
                ];
                foreach (var (name, value) in options)
                {
                    MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, name, value), $"set test option {name}");
                }
                MpvPlayer.Check(LibMpvNative.mpv_initialize(handle), "initialize test player");
                if (observe)
                    Observe();
                Player.Initialize();
            }
            catch
            {
                Player.Dispose();
                throw;
            }
        }

        internal void Observe() => Player.SourceEvent += notification => _events.Writer.TryWrite(notification);

        internal async Task<T> ReadAsync<T>(CancellationToken ct, Action<MpvSourceEvent>? inspect = null) where T : MpvSourceEvent
        {
            while (true)
            {
                var notification = await _events.Reader.ReadAsync(ct);
                inspect?.Invoke(notification);
                if (notification is T result)
                    return result;
            }
        }

        internal async Task<MpvSourceEvent> ReadTerminalAsync(CancellationToken ct)
        {
            while (true)
            {
                var notification = await _events.Reader.ReadAsync(ct);
                if (notification is MpvSourceEvent.Eof { Reached: true } or MpvSourceEvent.Ended)
                    return notification;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Player.Dispose();
            Assert.True(await Player.ShutdownCompletion.WaitAsync(Deadline));
        }
    }

    private sealed class LoopbackMedia : IDisposable
    {
        private readonly TcpListener _server = new(IPAddress.Loopback, 0);

        internal LoopbackMedia()
        {
            _server.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_server.LocalEndpoint).Port}/episode.wav";
        }

        internal string Url { get; }
        internal ValueTask<TcpClient> AcceptAsync(CancellationToken ct) => _server.AcceptTcpClientAsync(ct);

        internal async Task<string> ServeWaveAsync(CancellationToken ct)
        {
            using var client = await AcceptAsync(ct);
            var request = await ReadRequestAsync(client, ct);
            await WriteWaveAsync(client, truncate: false, ct);
            return request;
        }

        public void Dispose() => _server.Stop();
    }

    private static async Task WriteWaveAsync(TcpClient client, bool truncate, CancellationToken ct)
    {
        var data = CreateWave();
        var status = truncate
            ? $"206 Partial Content\r\nAccept-Ranges: bytes\r\nContent-Range: bytes 0-{data.Length - 1}/{data.Length}"
            : "200 OK";
        var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: {data.Length}\r\nContent-Type: audio/wav\r\nConnection: close\r\n\r\n");
        var stream = client.GetStream();
        await stream.WriteAsync(response, ct);
        await stream.WriteAsync(data.AsMemory(0, truncate ? data.Length * 3 / 4 : data.Length), ct);
    }

    private static async Task<string> ReadRequestAsync(TcpClient client, CancellationToken ct)
    {
        using var reader = new StreamReader(client.GetStream(), leaveOpen: true);
        var request = new StringBuilder();
        while (await reader.ReadLineAsync(ct) is { Length: > 0 } line)
            request.AppendLine(line);
        return request.ToString();
    }

    private static byte[] CreateWave()
    {
        const int sampleRate = 8000;
        const int payloadLength = sampleRate * 2 * 12;
        using var output = new MemoryStream(payloadLength + 44);
        using var writer = new BinaryWriter(output);
        writer.Write("RIFF"u8);
        writer.Write(payloadLength + 36);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(payloadLength);
        writer.Write(new byte[payloadLength]);
        return output.ToArray();
    }

    public void Dispose() => _directory.Delete(recursive: true);
}
