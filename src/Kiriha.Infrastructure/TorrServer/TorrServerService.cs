using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Models.TorrServer;
using Serilog;

namespace Kiriha.Infrastructure.TorrServer;

public sealed class TorrServerService : ITorrServerService
{
    private static readonly ILogger Logger = Log.ForContext<TorrServerService>();

    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private Process? _process;
    private bool _isDisposed;

    public bool IsRunning { get; private set; }

    public string ServerUrl
    {
        get
        {
            var url = _settingsService.Current.Torrents.Streaming.ServerUrl;
            if (string.IsNullOrWhiteSpace(url))
                return $"{Kiriha.Core.Domain.Constants.AppConstants.Api.TorrServer.LoopbackScheme}{Kiriha.Core.Domain.Constants.AppConstants.Api.TorrServer.DefaultHost}:{_settingsService.Current.Torrents.Streaming.ServerPort}";
            return url.TrimEnd('/');
        }
    }

    public TorrServerService(ISettingsService settingsService, HttpClient? httpClient = null)
    {
        _settingsService = settingsService;
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
                await ApplyMemorySettingsAsync(ct).ConfigureAwait(false);
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
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kiriha", "torrserver");
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
            _process = Process.Start(psi);

            // Wait up to 6 seconds for the server to become responsive
            var deadline = DateTime.UtcNow.AddSeconds(6);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (await PingServerAsync(ct).ConfigureAwait(false))
                {
                    IsRunning = true;
                    Logger.Information("TorrServerService: Local TorrServer started successfully");
                    await ApplyMemorySettingsAsync(ct).ConfigureAwait(false);
                    return true;
                }

                await Task.Delay(150, ct).ConfigureAwait(false);
            }

            Logger.Error("TorrServerService: Timed out waiting for TorrServer to respond on {Url}", ServerUrl);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Error(ex, "TorrServerService: Failed to ensure TorrServer is running");
            return false;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task<bool> PingServerAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(500));
            var response = await _httpClient.GetAsync($"{ServerUrl}/echo", cts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task ApplyMemorySettingsAsync(CancellationToken ct)
    {
        try
        {
            var cacheMb = Math.Clamp(_settingsService.Current.Torrents.Streaming.RamCacheMb, 50, 2048);
            long cacheBytes = (long)cacheMb * 1024L * 1024L;

            var payload = new
            {
                action = "set",
                sets = new
                {
                    CacheSize = cacheBytes,
                    ReaderReadAHead = 95,
                    PreloadCache = 50,
                    UseDisk = false,
                    RemoveCacheOnDrop = true
                }
            };

            var response = await _httpClient.PostAsJsonAsync($"{ServerUrl}/settings", payload, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                Logger.Information("TorrServerService: RAM streaming cache configured to {Mb} MB (0 bytes on disk)", cacheMb);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TorrServerService: Could not update TorrServer memory cache settings");
        }
    }

    public async Task<TorrServerTorrentStatus?> AddTorrentAsync(string link, string title, CancellationToken ct = default)
    {
        if (!await EnsureRunningAsync(ct).ConfigureAwait(false))
            return null;

        try
        {
            var payload = new
            {
                action = "add",
                link,
                title,
                save_to_db = false
            };

            var response = await _httpClient.PostAsJsonAsync($"{ServerUrl}/torrents", payload, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("TorrServerService: AddTorrent failed with HTTP {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct).ConfigureAwait(false);
            if (json == null)
                return null;

            var hash = json["hash"]?.GetValue<string>() ?? string.Empty;
            return await GetTorrentAsync(hash, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "TorrServerService: Error adding torrent '{Title}'", title);
            return null;
        }
    }

    public async Task<TorrServerTorrentStatus?> GetTorrentAsync(string hash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return null;

        try
        {
            var payload = new { action = "get", hash };
            var response = await _httpClient.PostAsJsonAsync($"{ServerUrl}/torrents", payload, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct).ConfigureAwait(false);
            if (json == null)
                return null;

            var files = new List<TorrServerFileItem>();
            if (json["file_stats"] is JsonArray fileArray)
            {
                foreach (var node in fileArray)
                {
                    if (node is JsonObject fileObj)
                    {
                        var id = fileObj["id"]?.GetValue<int>() ?? 0;
                        var path = fileObj["path"]?.GetValue<string>() ?? string.Empty;
                        var length = fileObj["length"]?.GetValue<long>() ?? 0L;
                        files.Add(new TorrServerFileItem
                        {
                            Id = id,
                            Path = path,
                            Length = length
                        });
                    }
                }
            }

            return new TorrServerTorrentStatus
            {
                Hash = json["hash"]?.GetValue<string>() ?? hash,
                Title = json["title"]?.GetValue<string>() ?? string.Empty,
                StatString = json["stat_string"]?.GetValue<string>() ?? string.Empty,
                Stat = json["stat"]?.GetValue<int>() ?? 0,
                TorrentSize = json["torrent_size"]?.GetValue<long>() ?? 0L,
                DownloadSpeed = json["download_speed"]?.GetValue<double>() ?? 0.0,
                UploadSpeed = json["upload_speed"]?.GetValue<double>() ?? 0.0,
                TotalPeers = json["total_peers"]?.GetValue<int>() ?? 0,
                ActivePeers = json["active_peers"]?.GetValue<int>() ?? 0,
                ConnectedSeeders = json["connected_seeders"]?.GetValue<int>() ?? 0,
                PreloadSize = json["preload_size"]?.GetValue<long>() ?? 0L,
                PreloadedBytes = json["preloaded_bytes"]?.GetValue<long>() ?? 0L,
                Files = files
            };
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TorrServerService: Error querying torrent {Hash}", hash);
            return null;
        }
    }

    public async Task<IReadOnlyList<TorrServerFileItem>> WaitForFilesAsync(string hash, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var status = await GetTorrentAsync(hash, ct).ConfigureAwait(false);
            if (status != null && status.Files.Count > 0)
                return status.Files;

            await Task.Delay(300, ct).ConfigureAwait(false);
        }

        return [];
    }

    public async Task DropTorrentAsync(string hash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return;

        try
        {
            var payload = new { action = "rem", hash };
            await _httpClient.PostAsJsonAsync($"{ServerUrl}/torrents", payload, ct).ConfigureAwait(false);
            Logger.Information("TorrServerService: Dropped torrent {Hash} from RAM cache", hash);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TorrServerService: Failed to drop torrent {Hash}", hash);
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
        _httpClient.Dispose();
    }

    private static string? LocateExecutable()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // 1. Output folder: torrserver/torrserver.exe
        var candidate1 = Path.Combine(baseDir, "torrserver", "torrserver.exe");
        if (File.Exists(candidate1))
            return candidate1;

        // 2. Application root folder: torrserver.exe
        var candidate2 = Path.Combine(baseDir, "torrserver.exe");
        if (File.Exists(candidate2))
            return candidate2;

        // 3. Dev folder: subprojects/torrserver/torrserver.exe
        var candidate3 = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "subprojects", "torrserver", "torrserver.exe"));
        if (File.Exists(candidate3))
            return candidate3;

        // 4. Fallback search up the directory tree
        var current = new DirectoryInfo(baseDir);
        for (int i = 0; i < 4 && current != null; i++)
        {
            var subprojectPath = Path.Combine(current.FullName, "subprojects", "torrserver", "torrserver.exe");
            if (File.Exists(subprojectPath))
                return subprojectPath;
            current = current.Parent;
        }

        return null;
    }
}
