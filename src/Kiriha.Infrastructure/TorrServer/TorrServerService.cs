using System.Diagnostics;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
using Serilog;

namespace Kiriha.Infrastructure.TorrServer;

public sealed partial class TorrServerService : ITorrServerService
{
    private static readonly ILogger Logger = Log.ForContext<TorrServerService>();
    private static readonly TimeSpan EchoPingTimeout = TimeSpan.FromSeconds(1);
    private const int StartupReadyTimeoutSeconds = 12;

    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private Process? _process;
    private bool _isDisposed;

    public bool IsRunning { get; private set; }

    public string ServerUrl
    {
        get
        {
            var url = _settingsService.Current.Torrents.Streaming.ServerUrl;
            var port = _settingsService.Current.Torrents.Streaming.ServerPort;
            if (string.IsNullOrWhiteSpace(url))
                return $"{AppConstants.Api.TorrServer.LoopbackScheme}{AppConstants.Api.TorrServer.DefaultHost}:{port}";

            url = url.TrimEnd('/');
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Host == "127.0.0.1" || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) &&
                uri.Port != port && port > 0)
            {
                var builder = new UriBuilder(uri) { Port = port };
                return builder.Uri.ToString().TrimEnd('/');
            }
            return url;
        }
    }

    public TorrServerService(ISettingsService settingsService, HttpClient? httpClient = null)
    {
        _settingsService = settingsService;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<bool> EnsureRunningAsync(CancellationToken ct = default)
    {
        if (IsRunning)
        {
            if (await PingServerAsync(ct).ConfigureAwait(false))
                return true;

            IsRunning = false;
        }

        await _lifecycleLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsRunning && await PingServerAsync(ct).ConfigureAwait(false))
                return true;

            // 1. Check if external or existing TorrServer is already running at ServerUrl
            if (await PingServerAsync(ct).ConfigureAwait(false))
            {
                Logger.Information("TorrServerService: External TorrServer detected at {Url}", ServerUrl);
                IsRunning = true;
                await ApplyMemorySettingsAsync(ct, isExternalInstance: true).ConfigureAwait(false);
                return true;
            }

            // 2. Start local process if enabled
            if (!_settingsService.Current.Torrents.Streaming.AutoStartServer)
            {
                Logger.Warning("TorrServerService: Server is not running and AutoStartServer is disabled");
                return false;
            }

            var exePath = LocateExecutable();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                Logger.Error("TorrServerService: torrserver.exe was not found. Please run scripts/download-torrserver.ps1");
                return false;
            }

            var port = _settingsService.Current.Torrents.Streaming.ServerPort;
            var dataDir = Platform.PathHelper.GetTorrServerDataPath();
            Directory.CreateDirectory(dataDir);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"-p {port} -d \"{dataDir}\" -i 127.0.0.1",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = dataDir
            };

            if (OperatingSystem.IsWindows())
            {
                psi.KillOnParentExit = true;
            }

            Logger.Information("TorrServerService: Launching local TorrServer on port {Port} from {Path}", port, exePath);
            try
            {
                _process = Process.Start(psi);
            }
            catch (Exception startEx) when (OperatingSystem.IsWindows() && psi.KillOnParentExit)
            {
                Logger.Warning(startEx, "TorrServerService: Process.Start with KillOnParentExit failed; retrying without JobObject binding");
                psi.KillOnParentExit = false;
                _process = Process.Start(psi);
            }

            if (_process == null)
            {
                Logger.Error("TorrServerService: Failed to start TorrServer process");
                return false;
            }

            // Wait up to StartupReadyTimeoutSeconds for the server to become responsive
            var deadline = DateTime.UtcNow.AddSeconds(StartupReadyTimeoutSeconds);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (_process.HasExited)
                {
                    Logger.Error("TorrServerService: TorrServer process exited prematurely with code {Code} (Path: {Path})", _process.ExitCode, exePath);
                    _process.Dispose();
                    _process = null;
                    return false;
                }

                if (await PingServerAsync(ct).ConfigureAwait(false))
                {
                    IsRunning = true;
                    Logger.Information("TorrServerService: Local TorrServer started successfully");
                    await ApplyMemorySettingsAsync(ct, isExternalInstance: false).ConfigureAwait(false);
                    return true;
                }

                await Task.Delay(150, ct).ConfigureAwait(false);
            }

            Logger.Error("TorrServerService: Timed out or canceled waiting for TorrServer to respond on {Url}", ServerUrl);
            Stop();
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "TorrServerService: Failed to ensure TorrServer is running");
            Stop();
            return false;
        }
        finally
        {
            _lifecycleLock.Release();
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
        try
        {
            if (_process != null && !_process.HasExited)
            {
                Logger.Information("TorrServerService: Stopping TorrServer process (PID: {Pid})", _process.Id);
                _process.Kill(entireProcessTree: true);
                _process.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TorrServerService: Error terminating TorrServer process");
        }
        finally
        {
            _process = null;
            IsRunning = false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        Stop();
        _lifecycleLock.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
