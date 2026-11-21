using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Kiriha.Infrastructure.Platform;
using Serilog;

namespace Kiriha.Infrastructure.TorrServer;

public sealed partial class TorrServerService : ITorrServerService
{
    private static readonly ILogger Logger = Log.ForContext<TorrServerService>();
    private static readonly TimeSpan EchoPingTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StartupReadyTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan StartupPollInterval = TimeSpan.FromMilliseconds(150);
    private const int ProcessExitTimeoutMs = 1000;

    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly Lock _processLock = new();
    private readonly Func<int, Process?> _startProcess;
    private readonly TimeSpan _startupTimeout;
    private readonly TorrServerUsage _usage;

    private Process? _process;
    private string? _managedUrl;
    private string? _readyUrl;
    private CancellationTokenSource? _startup;
    private int _stopGeneration;
    private bool _isDisposed;

    public bool IsRunning
    {
        get
        {
            lock (_processLock)
                return _readyUrl == ServerUrl && (_process == null || !_process.HasExited);
        }
    }

    public string ServerUrl
    {
        get
        {
            var streaming = _settingsService.Current.Torrents.Streaming;
            var url = streaming.ServerUrl;
            var port = streaming.ServerPort;
            if (string.IsNullOrWhiteSpace(url))
                return $"{AppConstants.Api.TorrServer.LoopbackScheme}{AppConstants.Api.TorrServer.DefaultHost}:{port}";

            url = url.TrimEnd('/');
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback && uri.Port != port && port > 0)
                return new UriBuilder(uri) { Port = port }.Uri.ToString().TrimEnd('/');
            return url;
        }
    }

    public TorrServerService(ISettingsService settingsService, HttpClient? httpClient = null)
        : this(settingsService, httpClient, StartLocalProcess, StartupReadyTimeout)
    {
    }

    internal TorrServerService(ISettingsService settingsService, HttpClient? httpClient,
        Func<int, Process?> startProcess, TimeSpan startupTimeout, string? usageDirectory = null,
        TimeSpan? releaseDelay = null)
    {
        _settingsService = settingsService;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _startProcess = startProcess;
        _startupTimeout = startupTimeout;
        _usage = new TorrServerUsage(usageDirectory ?? Path.Combine(PathHelper.GetSettingsDirPath(), "torrent-usage"),
            releaseDelay ?? TimeSpan.FromMilliseconds(600), RemoveTorrentAsync);
    }

    public async Task<bool> EnsureRunningAsync(CancellationToken ct = default)
    {
        int generation;
        lock (_processLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            generation = _stopGeneration;
        }

        await _lifecycleLock.WaitAsync(ct).ConfigureAwait(false);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            lock (_processLock)
            {
                if (generation != _stopGeneration)
                    return false;
                _startup = startup;
            }

            return await EnsureEndpointAsync(ServerUrl, startup.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (startup.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "TorrServerService: Failed to ensure TorrServer is running");
            return false;
        }
        finally
        {
            lock (_processLock)
            {
                if (ReferenceEquals(_startup, startup))
                    _startup = null;
            }
            _lifecycleLock.Release();
        }
    }

    private async Task<bool> EnsureEndpointAsync(string url, CancellationToken ct)
    {
        lock (_processLock)
        {
            ct.ThrowIfCancellationRequested();
            if (_process != null && (_managedUrl != url || _process.HasExited))
                StopProcess();
        }

        var probe = await ProbeServerAsync(url, ct).ConfigureAwait(false);
        if (probe == ServerProbe.Ready)
            return await MarkReadyAsync(url, ct).ConfigureAwait(false);

        lock (_processLock)
        {
            _readyUrl = null;
            ct.ThrowIfCancellationRequested();
            // Leave a running process available if a single probe failed during startup or load.
            if (_process == null)
            {
                if (probe == ServerProbe.OtherService ||
                    !_settingsService.Current.Torrents.Streaming.AutoStartServer ||
                    !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !CanStartLocally(uri) ||
                    !IsPortAvailable(uri.Port))
                {
                    Logger.Warning("TorrServerService: Cannot start a local server for {Url}", url);
                    return false;
                }

                _process = _startProcess(uri.Port);
                if (_process == null)
                    return false;
                _managedUrl = url;
            }
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_startupTimeout);
        bool ready = false;
        try
        {
            while (!deadline.IsCancellationRequested)
            {
                lock (_processLock)
                {
                    if (_process == null || _process.HasExited)
                    {
                        Logger.Warning("TorrServerService: Local server exited before becoming ready at {Url}", url);
                        return false;
                    }
                }

                if (await ProbeServerAsync(url, deadline.Token).ConfigureAwait(false) == ServerProbe.Ready)
                    return ready = await MarkReadyAsync(url, deadline.Token).ConfigureAwait(false);

                await Task.Delay(StartupPollInterval, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            Logger.Warning(ex, "TorrServerService: Local server startup timed out at {Url}", url);
        }
        finally
        {
            if (!ready)
            {
                lock (_processLock)
                    StopProcess();
            }
        }

        ct.ThrowIfCancellationRequested();
        return false;
    }

    private async Task<bool> MarkReadyAsync(string url, CancellationToken ct)
    {
        bool external;
        bool needsSettings;
        lock (_processLock)
        {
            external = _process == null;
            needsSettings = _readyUrl != url;
        }

        if (needsSettings)
        {
            // Keep the idle check and settings update atomic with tracked additions.
            await using var gate = await _usage.LockEndpointAsync(url, ct).ConfigureAwait(false);
            await ApplyMemorySettingsAsync(url, ct, external).ConfigureAwait(false);
        }

        lock (_processLock)
        {
            ct.ThrowIfCancellationRequested();
            if (ServerUrl != url || (!external && (_process == null || _process.HasExited)))
                return false;
            _readyUrl = url;
            return true;
        }
    }

    private static bool CanStartLocally(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp && uri.Port > 0 &&
        (uri.Host == AppConstants.Api.TorrServer.DefaultHost || uri.Host == "localhost") &&
        uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    private static bool IsPortAvailable(int port)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
        try
        {
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public string GetStreamUrl(string hash, int fileId, string fileName)
    {
        var escapedFileName = Uri.EscapeDataString(fileName);
        var indexParam = fileId >= 0 ? $"&index={fileId}" : string.Empty;
        return $"{ServerUrl}/stream/{escapedFileName}?link={hash}{indexParam}&play";
    }

    public void Stop()
    {
        lock (_processLock)
        {
            _stopGeneration++;
            _startup?.Cancel();
            StopProcess();
        }
    }

    private void StopProcess()
    {
        using var process = _process;
        _process = null;
        _managedUrl = null;
        _readyUrl = null;
        if (process == null)
            return;

        try
        {
            if (!process.HasExited)
            {
                Logger.Information("TorrServerService: Stopping TorrServer process (PID: {Pid})", process.Id);
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(ProcessExitTimeoutMs))
                    Logger.Warning("TorrServerService: Timed out waiting for process {Pid} to exit", process.Id);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TorrServerService: Error terminating TorrServer process");
        }
    }

    public void Dispose()
    {
        lock (_processLock)
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            Stop();
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }
    }
}
