using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models;
using Kiriha.Infrastructure.Player;
using Kiriha.Infrastructure.TorrServer;
using Kiriha.Services;
using Moq;

namespace Kiriha.Tests;

public sealed class PlaybackStartupRecoveryTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TorrServer_WhenLocalPortIsOccupied_DoesNotStartAChild()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new HttpClient(new ResponseHandler(_ => throw new HttpRequestException("No HTTP reply")));
        var processStarts = 0;
        using var server = new TorrServerService(Settings(port), client, _ => { processStarts++; return null; }, TestTimeout);

        Assert.False(await server.EnsureRunningAsync());
        Assert.Equal(0, processStarts);
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task TorrServer_WhenExistingServerCannotReportActivity_DoesNotResetItsSettings()
    {
        var requests = new List<string>();
        using var client = new HttpClient(new ResponseHandler(request =>
        {
            var path = Assert.IsType<Uri>(request.RequestUri).AbsolutePath;
            requests.Add(path);
            return path == "/echo" ? Reply("TorrServer MatriX.132") : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }));
        using var server = new TorrServerService(Settings(8090), client, _ => throw new InvalidOperationException(), TestTimeout);

        Assert.True(await server.EnsureRunningAsync());
        Assert.Equal(new[] { "/echo", "/torrents" }, requests);
        server.Stop();
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task TorrServer_AfterEarlyExitAndCancellation_ReusesExactlyOneReadyChild()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var timeout = new CancellationTokenSource(TestTimeout);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var childStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        int processStarts = 0;
        int healthyChildId = 0;
        using var client = new HttpClient(new ResponseHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath != "/echo")
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return Volatile.Read(ref processStarts) == 3
                ? Reply("TorrServer MatriX.132")
                : throw new HttpRequestException("Child is unavailable");
        }));
        using var server = new TorrServerService(Settings(port), client, _ =>
        {
            var attempt = Interlocked.Increment(ref processStarts);
            var info = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command",
                         attempt == 1 ? "exit 17" : "Start-Sleep -Seconds 30" })
                info.ArgumentList.Add(arg);
            var process = Process.Start(info) ?? throw new InvalidOperationException("Failed to start test child");
            if (attempt == 2)
                childStarted.SetResult(process.Id);
            if (attempt == 3)
                healthyChildId = process.Id;
            return process;
        }, TimeSpan.FromSeconds(5));

        Assert.False(await server.EnsureRunningAsync(timeout.Token));
        using var cancelStartup = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var cancelledStart = server.EnsureRunningAsync(cancelStartup.Token);
        using var cancelledChild = Process.GetProcessById(await childStarted.Task.WaitAsync(timeout.Token));
        await cancelStartup.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledStart);
        Assert.True(cancelledChild.HasExited);

        var attempts = await Task.WhenAll(server.EnsureRunningAsync(timeout.Token), server.EnsureRunningAsync(timeout.Token));
        Assert.All(attempts, ready => Assert.True(ready));
        Assert.Equal(3, processStarts);
        using var healthyChild = Process.GetProcessById(healthyChildId);
        server.Stop();
        Assert.True(healthyChild.HasExited);
    }

    [Fact]
    public async Task PlayerCommandPipe_TimesOutAndAcceptsTheNextCompleteRequest()
    {
        using var ct = new CancellationTokenSource(TestTimeout);
        var pipeName = Guid.NewGuid().ToString("N");
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new PlayerCommandServer(args => received.TrySetResult(args), pipeName, TimeSpan.FromMilliseconds(250));

        Assert.False(await PlayerProcessBridge.SendCommandAsync(pipeName, ["unavailable"], TimeSpan.FromMilliseconds(100), ct.Token));
        server.Start();
        using var incomplete = new NamedPipeClientStream(".", pipeName, PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await incomplete.ConnectAsync(ct.Token);

        string[] args = ["--player", "эпизод 02.mkv", PlayerProcessBridge.PlaybackRequestArg, "request-2"];
        Assert.True(await PlayerProcessBridge.SendCommandAsync(pipeName, args, TestTimeout, ct.Token));
        Assert.Equal(args, await received.Task.WaitAsync(ct.Token));
        await server.DisposeAsync().AsTask().WaitAsync(ct.Token);
        Assert.False(await PlayerProcessBridge.SendCommandAsync(pipeName, args, TimeSpan.FromMilliseconds(100), ct.Token));
    }

    [Fact]
    public async Task PlayerCommandPipe_CancellationInterruptsConnectionWait()
    {
        using var ct = new CancellationTokenSource();
        var pending = PlayerProcessBridge.SendCommandAsync(Guid.NewGuid().ToString("N"), ["--player"], TestTimeout, ct.Token);
        await ct.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private static ISettingsService Settings(int port)
    {
        var settings = new AppSettings();
        settings.Torrents.Streaming.AutoStartServer = true;
        settings.Torrents.Streaming.ServerPort = port;
        return Mock.Of<ISettingsService>(service => service.Current == settings);
    }

    private static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
