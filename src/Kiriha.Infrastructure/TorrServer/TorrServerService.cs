using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kiriha.Core.Abstractions.Services;
using Kiriha.Core.Domain.Constants;
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
            var port = _settingsService.Current.Torrents.Streaming.ServerPort;
            if (string.IsNullOrWhiteSpace(url))
                return $"{Kiriha.Core.Domain.Constants.AppConstants.Api.TorrServer.LoopbackScheme}{Kiriha.Core.Domain.Constants.AppConstants.Api.TorrServer.DefaultHost}:{port}";

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
            if (_process == null)
            {
                Logger.Error("TorrServerService: Failed to start TorrServer process");
                return false;
            }

            // Wait up to 6 seconds for the server to become responsive
            var deadline = DateTime.UtcNow.AddSeconds(6);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (_process.HasExited)
                {
                    Logger.Error("TorrServerService: TorrServer process exited prematurely with code {Code}", _process.ExitCode);
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

    internal async Task<bool> PingServerAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(500));
            var response = await _httpClient.GetAsync($"{ServerUrl}/echo", cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return false;

            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return body.Contains("MatriX", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task ApplyMemorySettingsAsync(CancellationToken ct, bool isExternalInstance = false)
    {
        try
        {
            if (isExternalInstance && await HasActiveTorrentsAsync(ct).ConfigureAwait(false))
            {
                Logger.Debug("TorrServerService: Active torrents detected on running TorrServer instance. Skipping settings reset.");
                return;
            }

            var cacheMb = Math.Clamp(_settingsService.Current.Torrents.Streaming.RamCacheMb, 50, 2048);
            long cacheBytes = (long)cacheMb * 1024L * 1024L;

            JsonObject? setsObj = null;
            try
            {
                var getResp = await _httpClient.PostAsJsonAsync($"{ServerUrl}/settings", new { action = "get" }, ct).ConfigureAwait(false);
                if (getResp.IsSuccessStatusCode)
                {
                    setsObj = await getResp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "TorrServerService: Could not query current settings before applying memory limits");
            }

            if (setsObj != null)
            {
                var currentCache = ReadInt64(setsObj["CacheSize"], 0L);
                var currentUseDisk = ReadBoolean(setsObj["UseDisk"], true);
                var currentRemoveOnDrop = ReadBoolean(setsObj["RemoveCacheOnDrop"], false);
                var currentTrackers = ReadString(setsObj["DefaultTrackers"], string.Empty);

                bool hasTrackers = !string.IsNullOrWhiteSpace(currentTrackers) && currentTrackers.Contains("nyaa.tracker.wf", StringComparison.OrdinalIgnoreCase);

                // If attaching to an already running RAM-configured server, or if settings already match, DO NOT call action: "set".
                // Calling "set" causes TorrServer to drop all torrents, disconnect all peers, and restart its BitTorrent client.
                if ((isExternalInstance && !currentUseDisk) ||
                    (currentCache == cacheBytes && !currentUseDisk && currentRemoveOnDrop && hasTrackers))
                {
                    Logger.Debug("TorrServerService: Settings already match RAM streaming requirements. Preserving active client session.");
                    return;
                }

                setsObj["CacheSize"] = cacheBytes;
                setsObj["ReaderReadAHead"] = 95;
                setsObj["PreloadCache"] = 50;
                setsObj["UseDisk"] = false;
                setsObj["RemoveCacheOnDrop"] = true;
                setsObj["RetrackersMode"] = 1;
                setsObj["ResponsiveMode"] = true;

                if (string.IsNullOrWhiteSpace(currentTrackers))
                {
                    setsObj["DefaultTrackers"] = AppConstants.Torrents.DefaultPublicTrackers;
                }
                else if (!currentTrackers.Contains("nyaa.tracker.wf", StringComparison.OrdinalIgnoreCase))
                {
                    setsObj["DefaultTrackers"] = $"{AppConstants.Torrents.DefaultPublicTrackers}\n{currentTrackers}";
                }
            }
            else if (isExternalInstance)
            {
                Logger.Debug("TorrServerService: Skipping blind settings override on external TorrServer instance.");
                return;
            }
            else
            {
                setsObj = new JsonObject
                {
                    ["CacheSize"] = cacheBytes,
                    ["ReaderReadAHead"] = 95,
                    ["PreloadCache"] = 50,
                    ["UseDisk"] = false,
                    ["RemoveCacheOnDrop"] = true,
                    ["RetrackersMode"] = 1,
                    ["ResponsiveMode"] = true,
                    ["DefaultTrackers"] = AppConstants.Torrents.DefaultPublicTrackers
                };
            }

            var payload = new
            {
                action = "set",
                sets = setsObj
            };

            var response = await _httpClient.PostAsJsonAsync($"{ServerUrl}/settings", payload, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                Logger.Information("TorrServerService: RAM streaming cache configured to {Mb} MB (0 disk bytes, public trackers restored)", cacheMb);
                // Allow TorrServer BitTorrent client a moment to finish its internal reload ("end set settings")
                await Task.Delay(800, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TorrServerService: Could not update TorrServer memory cache settings");
        }
    }

    private async Task<bool> HasActiveTorrentsAsync(CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"{ServerUrl}/torrents", new { action = "list" }, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return false;

            var array = await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken: ct).ConfigureAwait(false);
            return array is { Count: > 0 };
        }
        catch
        {
            return false;
        }
    }

    public async Task<TorrServerTorrentStatus?> AddTorrentAsync(string link, string title, CancellationToken ct = default)
    {
        if (!await EnsureRunningAsync(ct).ConfigureAwait(false))
            return null;

        try
        {
            var finalLink = link;
            if (finalLink.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) &&
                !finalLink.Contains("&tr=", StringComparison.OrdinalIgnoreCase))
            {
                finalLink = $"{finalLink}{AppConstants.Torrents.NyaaDefaultTrackersQuery}";
            }

            var payload = new
            {
                action = "add",
                link = finalLink,
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

            var hash = ReadString(json["hash"], string.Empty);
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
                        var id = ReadInt32(fileObj["id"], 0);
                        var path = ReadString(fileObj["path"], string.Empty);
                        var length = ReadInt64(fileObj["length"], 0L);
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
                Hash = ReadString(json["hash"], hash),
                Title = ReadString(json["title"], string.Empty),
                StatString = ReadString(json["stat_string"], string.Empty),
                Stat = ReadInt32(json["stat"], 0),
                TorrentSize = ReadInt64(json["torrent_size"], 0L),
                DownloadSpeed = ReadDouble(json["download_speed"], 0.0),
                UploadSpeed = ReadDouble(json["upload_speed"], 0.0),
                TotalPeers = ReadInt32(json["total_peers"], 0),
                ActivePeers = ReadInt32(json["active_peers"], 0),
                ConnectedSeeders = ReadInt32(json["connected_seeders"], 0),
                PreloadSize = ReadInt64(json["preload_size"], 0L),
                PreloadedBytes = ReadInt64(json["preloaded_bytes"], 0L),
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
        for (int i = 0; i < 6 && current != null; i++)
        {
            var subprojectPath = Path.Combine(current.FullName, "subprojects", "torrserver", "torrserver.exe");
            if (File.Exists(subprojectPath))
                return subprojectPath;
            current = current.Parent;
        }

        return null;
    }

    private static int ReadInt32(JsonNode? node, int fallback = 0)
    {
        if (node is not JsonValue val) return fallback;
        if (val.TryGetValue<int>(out var i)) return i;
        if (val.TryGetValue<long>(out var l) && l is >= int.MinValue and <= int.MaxValue) return (int)l;
        if (val.TryGetValue<double>(out var d) && double.IsFinite(d)) return (int)d;
        if (val.TryGetValue<JsonElement>(out var el))
        {
            if (el.ValueKind == JsonValueKind.Number)
            {
                if (el.TryGetInt32(out var elInt)) return elInt;
                if (el.TryGetInt64(out var elLong) && elLong is >= int.MinValue and <= int.MaxValue) return (int)elLong;
                if (el.TryGetDouble(out var elDouble) && double.IsFinite(elDouble)) return (int)elDouble;
            }
            else if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }
        return fallback;
    }

    private static long ReadInt64(JsonNode? node, long fallback = 0L)
    {
        if (node is not JsonValue val) return fallback;
        if (val.TryGetValue<long>(out var l)) return l;
        if (val.TryGetValue<int>(out var i)) return i;
        if (val.TryGetValue<double>(out var d) && double.IsFinite(d)) return (long)d;
        if (val.TryGetValue<JsonElement>(out var el))
        {
            if (el.ValueKind == JsonValueKind.Number)
            {
                if (el.TryGetInt64(out var elLong)) return elLong;
                if (el.TryGetDouble(out var elDouble) && double.IsFinite(elDouble)) return (long)elDouble;
            }
            else if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }
        return fallback;
    }

    private static double ReadDouble(JsonNode? node, double fallback = 0.0)
    {
        if (node is not JsonValue val) return fallback;
        if (val.TryGetValue<double>(out var d) && double.IsFinite(d)) return d;
        if (val.TryGetValue<long>(out var l)) return l;
        if (val.TryGetValue<int>(out var i)) return i;
        if (val.TryGetValue<JsonElement>(out var el))
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var elDouble) && double.IsFinite(elDouble))
                return elDouble;
            if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
                return parsed;
        }
        return fallback;
    }

    private static bool ReadBoolean(JsonNode? node, bool fallback = false)
    {
        if (node is not JsonValue val) return fallback;
        if (val.TryGetValue<bool>(out var b)) return b;
        if (val.TryGetValue<JsonElement>(out var el))
        {
            if (el.ValueKind == JsonValueKind.True) return true;
            if (el.ValueKind == JsonValueKind.False) return false;
            if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var parsed)) return parsed;
        }
        return fallback;
    }

    private static string ReadString(JsonNode? node, string fallback = "")
    {
        if (node is not JsonValue val) return fallback;
        if (val.TryGetValue<string>(out var s) && s != null) return s;
        if (val.TryGetValue<JsonElement>(out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? fallback;
        return fallback;
    }
}
