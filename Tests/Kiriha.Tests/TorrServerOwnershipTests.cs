using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Infrastructure.TorrServer;
using Moq;

namespace Kiriha.Tests;

public sealed class TorrServerOwnershipTests : IDisposable
{
    private const string FirstHash = "0123456789abcdef0123456789abcdef01234567";
    private const string SecondHash = "fedcba9876543210fedcba9876543210fedcba98";
    private const string Endpoint = "http://127.0.0.1:8090";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("Kiriha.TorrentUsage.");

    [Fact]
    public async Task TrackerAndTwoPlayerSessions_ReleaseOnlyAfterTheLastWindowCloses()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        using var server = new TorrentApi();
        using var tracker = CreateService(server.Client);
        using var firstWindow = CreateService(server.Client);
        var otherSettings = new AppSettings();
        otherSettings.Torrents.Streaming.ServerPort = 8091;
        using var secondWindow = CreateService(server.Client, otherSettings);

        using var prepared = await PrepareAsync(tracker, FirstHash, ct);
        var url = tracker.GetStreamUrl(FirstHash, 1, "episode.mkv");
        using var first = await firstWindow.AcquirePlaybackAsync(url, FirstHash, ct);
        using var second = await secondWindow.AcquirePlaybackAsync(url.Replace("127.0.0.1", "localhost"), FirstHash.ToUpperInvariant(), ct);
        prepared.Dispose();
        await tracker.FlushTorrentReleasesAsync(ct);
        await tracker.DropTorrentAsync(FirstHash, ct);
        Assert.Empty(server.Removals);

        first.Dispose();
        await firstWindow.FlushTorrentReleasesAsync(ct);
        Assert.Empty(server.Removals);

        second.Dispose();
        await secondWindow.FlushTorrentReleasesAsync(ct);
        Assert.Equal([FirstHash], server.Removals.Select(call => call.Hash));
    }

    [Fact]
    public async Task RapidReturnToPreviousTorrent_DoesNotRemoveItDuringTheSecondPreparation()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        using var server = new TorrentApi();
        using var tracker = CreateService(server.Client);
        using var player = CreateService(server.Client);

        using var original = await player.AcquirePlaybackAsync(player.GetStreamUrl(FirstHash, 1, "first.mkv"), FirstHash, ct);
        using var second = await player.AcquirePlaybackAsync(player.GetStreamUrl(SecondHash, 1, "second.mkv"), SecondHash, ct);
        using var returning = await PrepareAsync(tracker, FirstHash, ct);
        original.Dispose();
        second.Dispose();
        await player.FlushTorrentReleasesAsync(ct);
        Assert.Equal([SecondHash], server.Removals.Select(call => call.Hash));

        using var reopened = await player.AcquirePlaybackAsync(player.GetStreamUrl(FirstHash, 2, "first.mkv"), FirstHash, ct);
        returning.Dispose();
        await tracker.FlushTorrentReleasesAsync(ct);
        Assert.Single(server.Removals);

        reopened.Dispose();
        await player.FlushTorrentReleasesAsync(ct);
        Assert.Equal([SecondHash, FirstHash], server.Removals.Select(call => call.Hash));
    }

    [Fact]
    public async Task RemovalAlreadySent_NewPreparationWaitsAndReaddsAfterIt()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        var removalStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRemoval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new TorrentApi
        {
            BeforeAction = async (action, token) =>
            {
                if (action == "rem")
                {
                    removalStarted.TrySetResult();
                    await allowRemoval.Task.WaitAsync(token);
                }
            }
        };
        using var oldPlayer = CreateService(server.Client);
        using var tracker = CreateService(server.Client);
        using var old = await oldPlayer.AcquirePlaybackAsync(oldPlayer.GetStreamUrl(FirstHash, 1, "old.mkv"), FirstHash, ct);
        old.Dispose();
        await removalStarted.Task.WaitAsync(ct);
        var preparation = PrepareAsync(tracker, FirstHash, ct);
        try
        {
            Assert.False(preparation.IsCompleted);
            Assert.DoesNotContain(server.Calls, call => call.Action == "add");
        }
        finally
        {
            allowRemoval.TrySetResult();
        }

        using var prepared = await preparation.WaitAsync(ct);
        await oldPlayer.FlushTorrentReleasesAsync(ct);
        Assert.Equal(["rem", "add"], server.Calls.Select(call => call.Action));
        prepared.Dispose();
        await tracker.FlushTorrentReleasesAsync(ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledAdd_ReleasesTheLateResultWithoutRemovingAnExistingStream(bool hasPlayer)
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        var additionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmAddition = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new TorrentApi
        {
            BeforeAction = async (action, token) =>
            {
                if (action == "add")
                {
                    additionStarted.TrySetResult();
                    await confirmAddition.Task.WaitAsync(token);
                }
            }
        };
        using var tracker = CreateService(server.Client);
        using var player = CreateService(server.Client);
        using var active = hasPlayer
            ? await player.AcquirePlaybackAsync(player.GetStreamUrl(FirstHash, 1, "existing.mkv"), FirstHash, ct)
            : null;

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pending = tracker.PrepareTorrentAsync(Magnet(FirstHash), "late", cancel.Token);
        await additionStarted.Task.WaitAsync(ct);
        try
        {
            await cancel.CancelAsync();
        }
        finally
        {
            confirmAddition.TrySetResult();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await tracker.FlushTorrentReleasesAsync(ct);
        Assert.Equal(hasPlayer ? 0 : 1, server.Removals.Count());

        active?.Dispose();
        await player.FlushTorrentReleasesAsync(ct);
        Assert.Single(server.Removals);
    }

    [Fact]
    public async Task Release_UsesTheOriginalServerAfterSettingsChange()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        using var server = new TorrentApi();
        var settings = new AppSettings();
        using var service = CreateService(server.Client, settings);
        using var first = await service.AcquirePlaybackAsync(service.GetStreamUrl(FirstHash, 1, "first.mkv"), FirstHash, ct);
        settings.Torrents.Streaming.ServerPort = 8091;
        using var second = await service.AcquirePlaybackAsync(service.GetStreamUrl(FirstHash, 1, "first.mkv"), FirstHash, ct);

        first.Dispose();
        await service.FlushTorrentReleasesAsync(ct);
        Assert.Equal(Endpoint, Assert.Single(server.Removals).Endpoint);

        second.Dispose();
        await service.FlushTorrentReleasesAsync(ct);
        Assert.Equal([Endpoint, "http://127.0.0.1:8091"], server.Removals.Select(call => call.Endpoint));
    }

    [Fact]
    public async Task Release_RetriesTemporaryHttpFailureAndRemovesTheMarkerOnlyAfterSuccess()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        var attempts = 0;
        using var server = new TorrentApi
        {
            RemovalStatus = () => Interlocked.Increment(ref attempts) == 1
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK
        };
        using var service = CreateService(server.Client);
        using var lease = await service.AcquirePlaybackAsync(service.GetStreamUrl(FirstHash, 1, "episode.mkv"), FirstHash, ct);
        lease.Dispose();
        await service.FlushTorrentReleasesAsync(ct);
        Assert.Equal(2, attempts);
        Assert.Empty(_directory.GetFiles("*.usage"));
    }

    [Fact]
    public async Task Release_RespectsAReadHandleInAnotherProcess()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var ct = timeout.Token;
        using var server = new TorrentApi();
        using var service = CreateService(server.Client);
        using var lease = await service.AcquirePlaybackAsync(service.GetStreamUrl(FirstHash, 1, "episode.mkv"), FirstHash, ct);
        var marker = Assert.Single(_directory.GetFiles("*.usage"));
        const string script = """
            $lease = [IO.File]::Open($env:KIRIHA_TORRENT_TEST_MARKER, 'Open', 'Read', 'Read')
            try {
                [Console]::WriteLine('ready')
                [Console]::ReadLine() | Out-Null
            }
            finally { $lease.Dispose() }
            """;
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        };
        start.Environment["KIRIHA_TORRENT_TEST_MARKER"] = marker.FullName;
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            start.ArgumentList.Add(arg);

        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the marker holder.");
        try
        {
            Assert.Equal("ready", await child.StandardOutput.ReadLineAsync(ct));
            lease.Dispose();
            await service.FlushTorrentReleasesAsync(ct);
            Assert.Empty(server.Removals);

            await child.StandardInput.WriteLineAsync();
            await child.WaitForExitAsync(ct);
            Assert.Equal(0, child.ExitCode);
            await service.DropTorrentAsync(FirstHash, ct);
            Assert.Single(server.Removals);
        }
        finally
        {
            if (!child.HasExited)
                child.Kill(entireProcessTree: true);
        }
    }

    private TorrServerService CreateService(HttpClient client, AppSettings? settings = null)
    {
        var current = settings ?? new AppSettings();
        var service = Mock.Of<ISettingsService>(settingsService => settingsService.Current == current);
        return new TorrServerService(service, client, _ => throw new InvalidOperationException("A test server already exists."),
            Deadline, _directory.FullName, releaseDelay: TimeSpan.Zero);
    }

    private static async Task<TorrServerPreparation> PrepareAsync(TorrServerService service, string hash, CancellationToken ct) =>
        await service.PrepareTorrentAsync(Magnet(hash), "episode", ct) ??
        throw new InvalidOperationException("The test torrent was not added.");

    private static string Magnet(string hash) => $"magnet:?xt=urn:btih:{hash}";

    public void Dispose() => _directory.Delete(recursive: true);

    private sealed record ApiCall(string Endpoint, string Action, string Hash);

    private sealed class TorrentApi : HttpMessageHandler
    {
        private readonly ConcurrentQueue<ApiCall> _calls = new();
        private HttpClient? _client;

        internal HttpClient Client => _client ??= new HttpClient(this, disposeHandler: false);
        internal IReadOnlyCollection<ApiCall> Calls => _calls;
        internal IEnumerable<ApiCall> Removals => _calls.Where(call => call.Action == "rem");
        internal Func<string, CancellationToken, Task>? BeforeAction { get; init; }
        internal Func<HttpStatusCode>? RemovalStatus { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request is missing its URL.");
            if (uri.AbsolutePath == "/echo")
                return Reply("TorrServer MatriX.132");

            var content = request.Content ?? throw new InvalidOperationException("Request is missing its payload.");
            using var json = JsonDocument.Parse(await content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            var action = root.GetProperty("action").GetString() ?? string.Empty;
            if (action == "list")
                return Reply("[]");
            if (action == "get")
                return Reply("{}");

            var hash = action == "add"
                ? (root.GetProperty("link").GetString() ?? string.Empty).Split(':')[^1].Split('&')[0]
                : root.GetProperty("hash").GetString() ?? string.Empty;
            _calls.Enqueue(new ApiCall(uri.GetLeftPart(UriPartial.Authority), action, hash));
            if (BeforeAction is { } before)
                await before(action, ct);

            if (action == "rem" && RemovalStatus is { } status)
                return new HttpResponseMessage(status());

            return Reply(JsonSerializer.Serialize(new { hash }));
        }

        private static HttpResponseMessage Reply(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body) };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _client?.Dispose();
            base.Dispose(disposing);
        }
    }
}
